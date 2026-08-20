-- =============================================================================
-- 04_procs.sql — business logic that lives in stored procedures
--
-- Exhibits:
--   SP_GET_CUST_EXPO — customer exposure; DISAGREES with the credit trigger
--                      about the meaning of status 'X' (tour #3)
--   SP_REL_ORD_HLD   — piece 3 of 3 of the order-hold logic; contains the
--                      undocumented 110% tolerance rule (tour #1)
--   SP_CANC_ORD      — "cancel" = soft delete: sets STS_FLG 'X' (tour #5)
--   SP_GET_ITM_AVL   — item availability; hardcoded to warehouse MAIN, a relic
--                      from when there was only one warehouse (tour #2)
--   SP_CALC_ORD_TOT  — the "official" order total; disagrees with the line
--                      trigger about discounts and rounding (tour #4)
-- =============================================================================
USE ERPPRD01;
GO

-- -----------------------------------------------------------------------------
-- SP_GET_CUST_EXPO — total exposure for a customer account
--
-- Includes status 'X' orders: this author read 'X' as "exception, still
-- potentially live, count it to be safe". The credit trigger excludes 'X'
-- ("cancelled"). SP_CANC_ORD agrees with the trigger. This proc does not.
-- Practical effect: cancelling an order does NOT lower the exposure that
-- SP_REL_ORD_HLD checks, so a customer can stay blocked by orders that
-- everyone believes are gone.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.SP_GET_CUST_EXPO
    @CUST_ACCT_ID INT,
    @EXPO_AMT     NUMERIC(15,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @EXPO_AMT = ISNULL(SUM(ORD_TOT_AMT), 0)
      FROM dbo.OE_ORD_HDR
     WHERE CUST_ACCT_ID = @CUST_ACCT_ID
       AND STS_FLG IN ('N','H','X');            -- 'X' included HERE
END;
GO

-- -----------------------------------------------------------------------------
-- SP_REL_ORD_HLD — the only sanctioned way to release an order from hold
--
-- Rules enforced here and NOWHERE ELSE (institutional memory in executable
-- form):
--   * only orders in 'H' can be released
--   * for credit holds, release is allowed only if the customer's exposure
--     (per SP_GET_CUST_EXPO, 'X' included!) does not exceed 110% of the
--     credit limit. The 10% tolerance was agreed verbally with the CFO in
--     2015. There is no document. There is only this WHERE clause.
--   * writes the audit trail (the triggers don't)
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.SP_REL_ORD_HLD
    @ORD_HDR_ID INT,
    @USR_NM     VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STS CHAR(1), @RSN VARCHAR(4), @CUST INT;
    SELECT @STS = STS_FLG, @RSN = HLD_RSN_CD, @CUST = CUST_ACCT_ID
      FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = @ORD_HDR_ID;

    IF @STS IS NULL
    BEGIN
        RAISERROR('SP_REL_ORD_HLD: order %d not found.', 16, 1, @ORD_HDR_ID);
        RETURN;
    END;

    IF @STS <> 'H'
    BEGIN
        RAISERROR('SP_REL_ORD_HLD: order %d is not on hold (status %s).', 16, 1, @ORD_HDR_ID, @STS);
        RETURN;
    END;

    IF @RSN = 'CR'
    BEGIN
        DECLARE @EXPO NUMERIC(15,2), @LMT NUMERIC(15,2);
        EXEC dbo.SP_GET_CUST_EXPO @CUST, @EXPO OUTPUT;
        SELECT @LMT = CR_LMT_AMT FROM dbo.AR_CUST_ACCT WHERE CUST_ACCT_ID = @CUST;

        IF @LMT IS NOT NULL AND @EXPO > @LMT * 1.10   -- the verbal-agreement tolerance
        BEGIN
            RAISERROR('SP_REL_ORD_HLD: credit release refused, exposure exceeds 110%% of limit.', 16, 1);
            RETURN;
        END;
    END;

    UPDATE dbo.OE_ORD_HDR
       SET STS_FLG = 'N', HLD_RSN_CD = NULL,
           LST_UPD_BY = @USR_NM, LST_UPD_DT = GETDATE()
     WHERE ORD_HDR_ID = @ORD_HDR_ID;

    INSERT INTO dbo.FND_AUDIT_TRL (OBJ_NM, OBJ_ID, ACTN_CD, ACTN_BY, RMK_TXT)
    VALUES ('OE_ORD_HDR', @ORD_HDR_ID, 'REL_HLD', @USR_NM, 'Order released from hold');
END;
GO

-- -----------------------------------------------------------------------------
-- SP_CANC_ORD — cancel an order (soft delete)
--
-- Nothing in this system ever issues DELETE. Cancellation is STS_FLG = 'X'
-- on the header and its lines, plus the release of committed stock.
-- This author read 'X' as "cancelled, gone". See SP_GET_CUST_EXPO for the
-- opposing school of thought.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.SP_CANC_ORD
    @ORD_HDR_ID INT,
    @USR_NM     VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = @ORD_HDR_ID AND STS_FLG IN ('N','H'))
    BEGIN
        RAISERROR('SP_CANC_ORD: order %d not found or not cancellable.', 16, 1, @ORD_HDR_ID);
        RETURN;
    END;

    -- release committed stock (from MAIN, where commitments are assumed to live)
    UPDATE oh
       SET oh.QTY_COMM  = CASE WHEN oh.QTY_COMM >= l.QTY_ORD THEN oh.QTY_COMM - l.QTY_ORD ELSE 0 END,
           oh.LST_UPD_BY = @USR_NM, oh.LST_UPD_DT = GETDATE()
      FROM dbo.INV_ONHAND_QTY oh
      JOIN dbo.OE_ORD_LINE l ON l.ITEM_ID = oh.ITEM_ID AND l.ORD_HDR_ID = @ORD_HDR_ID
     WHERE oh.WHSE_CD = 'MAIN' AND l.STS_FLG <> 'X';

    UPDATE dbo.OE_ORD_LINE
       SET STS_FLG = 'X', LST_UPD_BY = @USR_NM, LST_UPD_DT = GETDATE()
     WHERE ORD_HDR_ID = @ORD_HDR_ID;

    UPDATE dbo.OE_ORD_HDR
       SET STS_FLG = 'X', LST_UPD_BY = @USR_NM, LST_UPD_DT = GETDATE()
     WHERE ORD_HDR_ID = @ORD_HDR_ID;

    INSERT INTO dbo.FND_AUDIT_TRL (OBJ_NM, OBJ_ID, ACTN_CD, ACTN_BY, RMK_TXT)
    VALUES ('OE_ORD_HDR', @ORD_HDR_ID, 'CANC', @USR_NM, 'Order cancelled (soft delete)');
END;
GO

-- -----------------------------------------------------------------------------
-- SP_GET_ITM_AVL — available-to-promise for an item
--
-- available = on hand - committed... in warehouse MAIN only. The WHERE clause
-- dates from 2011, when MAIN was the only warehouse. SEC1, TRNS and QC01 were
-- added later; nobody revisited this proc. Stock sitting in SEC1 or arriving
-- in TRNS is invisible to it. Everyone in the office "knows" you have to
-- mentally add the transit quantity. The database does not know that.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.SP_GET_ITM_AVL
    @ITEM_CD VARCHAR(30),
    @AVL_QTY NUMERIC(15,3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @AVL_QTY = ISNULL(SUM(oh.QTY_OH - oh.QTY_COMM), 0)
      FROM dbo.INV_ONHAND_QTY oh
      JOIN dbo.INV_ITEM_MST   im ON im.ITEM_ID = oh.ITEM_ID
     WHERE im.ITEM_CD = @ITEM_CD
       AND oh.WHSE_CD = 'MAIN';                 -- 2011 called, it wants its warehouse back
END;
GO

-- -----------------------------------------------------------------------------
-- SP_CALC_ORD_TOT — the "official" order total recalculation
--
-- Differences from TRG_OE_ORD_LINE_AIU (which also maintains ORD_TOT_AMT):
--   1. COMPOUNDS line discount and header discount (the trigger takes the max)
--   2. rounds ONCE at the end (the trigger rounds per line)
-- Run this proc and the stored total changes. Touch a line and the trigger
-- changes it back. Both authors were sure they were right.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.SP_CALC_ORD_TOT
    @ORD_HDR_ID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE h
       SET h.ORD_TOT_AMT = x.TOT,
           h.LST_UPD_BY  = 'SP_CALC',
           h.LST_UPD_DT  = GETDATE()
      FROM dbo.OE_ORD_HDR h
      JOIN (
             SELECT l.ORD_HDR_ID,
                    ROUND(SUM(
                        l.QTY_ORD * l.UNIT_PRC
                        * (1 - l.LN_DSC_PCT / 100.0)
                        * (1 - h2.DSC_PCT   / 100.0)
                    ), 2) AS TOT
               FROM dbo.OE_ORD_LINE l
               JOIN dbo.OE_ORD_HDR  h2 ON h2.ORD_HDR_ID = l.ORD_HDR_ID
              WHERE l.STS_FLG <> 'X'
              GROUP BY l.ORD_HDR_ID
           ) x ON x.ORD_HDR_ID = h.ORD_HDR_ID
     WHERE h.ORD_HDR_ID = @ORD_HDR_ID;
END;
GO

PRINT '04_procs.sql completed.';
GO
