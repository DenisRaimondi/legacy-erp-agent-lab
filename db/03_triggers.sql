-- =============================================================================
-- 03_triggers.sql — business logic that lives in triggers
--
-- Two exhibits:
--   TRG_OE_ORD_HDR_AI  — piece 2 of 3 of the order-hold logic (credit check)
--   TRG_OE_ORD_LINE_AIU — the "other" order total calculation, which disagrees
--                         with SP_CALC_ORD_TOT (see tour-of-the-mess.md #4)
--
-- Neither trigger writes to FND_AUDIT_TRL: they predate it (2012) and were
-- never retrofitted. State changes made here leave no audit trace.
-- =============================================================================
USE ERPPRD01;
GO

-- -----------------------------------------------------------------------------
-- TRG_OE_ORD_HDR_AI — credit limit check on order insert
--
-- Exposure = sum of ORD_TOT_AMT over the customer's orders with status
-- 'N' or 'H'. Orders in 'X' are NOT counted here (this author considered X
-- to mean "cancelled"). Compare with SP_GET_CUST_EXPO in 04_procs.sql, whose
-- author disagreed. The two run different numbers for the same customer.
--
-- Known quirk kept for authenticity: the trigger sets the hold but the reason
-- code only since "the 2014 fix" — the seed data contains an older hold with
-- HLD_RSN_CD NULL, exactly what this trigger used to produce.
-- -----------------------------------------------------------------------------
CREATE OR ALTER TRIGGER dbo.TRG_OE_ORD_HDR_AI
ON dbo.OE_ORD_HDR
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE h
       SET h.STS_FLG    = 'H',
           h.HLD_RSN_CD = 'CR',
           h.LST_UPD_BY = 'TRG_CR',
           h.LST_UPD_DT = GETDATE()
      FROM dbo.OE_ORD_HDR h
      JOIN inserted i ON i.ORD_HDR_ID = h.ORD_HDR_ID
      JOIN dbo.AR_CUST_ACCT c ON c.CUST_ACCT_ID = i.CUST_ACCT_ID
     WHERE c.CR_LMT_AMT IS NOT NULL
       AND c.CR_LMT_AMT > 0
       AND (
             SELECT ISNULL(SUM(o.ORD_TOT_AMT), 0)
               FROM dbo.OE_ORD_HDR o
              WHERE o.CUST_ACCT_ID = i.CUST_ACCT_ID
                AND o.STS_FLG IN ('N','H')          -- 'X' excluded HERE
           ) > c.CR_LMT_AMT;
END;
GO

-- -----------------------------------------------------------------------------
-- TRG_OE_ORD_LINE_AIU — recompute the denormalized header total on line change
--
-- THIS IS NOT THE SAME FORMULA AS SP_CALC_ORD_TOT. Differences:
--   1. Discount rule: this trigger applies MAX(line discount, header discount)
--      ("the better discount wins" — a misreading of the 2016 commercial
--      policy). The proc COMPOUNDS both discounts.
--   2. Rounding: this trigger rounds PER LINE and then sums. The proc rounds
--      once at the end.
-- Whichever code path touched the order LAST decided what ORD_TOT_AMT is.
-- -----------------------------------------------------------------------------
CREATE OR ALTER TRIGGER dbo.TRG_OE_ORD_LINE_AIU
ON dbo.OE_ORD_LINE
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE h
       SET h.ORD_TOT_AMT = x.TOT,
           h.LST_UPD_BY  = 'TRG_TOT',
           h.LST_UPD_DT  = GETDATE()
      FROM dbo.OE_ORD_HDR h
      JOIN (
             SELECT l.ORD_HDR_ID,
                    SUM(ROUND(
                        l.QTY_ORD * l.UNIT_PRC *
                        (1 - (CASE WHEN l.LN_DSC_PCT > h2.DSC_PCT
                                   THEN l.LN_DSC_PCT
                                   ELSE h2.DSC_PCT END) / 100.0)
                    , 2)) AS TOT
               FROM dbo.OE_ORD_LINE l
               JOIN dbo.OE_ORD_HDR  h2 ON h2.ORD_HDR_ID = l.ORD_HDR_ID
              WHERE l.STS_FLG <> 'X'
              GROUP BY l.ORD_HDR_ID
           ) x ON x.ORD_HDR_ID = h.ORD_HDR_ID
     WHERE h.ORD_HDR_ID IN (SELECT DISTINCT ORD_HDR_ID FROM inserted);
END;
GO

PRINT '03_triggers.sql completed.';
GO
