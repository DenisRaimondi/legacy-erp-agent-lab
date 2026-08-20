-- =============================================================================
-- 99_verify.sql — proves every documented trap actually fires
--
-- Read-only except for exhibit #4, which mutates ORD_TOT_AMT of order 1046
-- back and forth to demonstrate the proc/trigger fight (and leaves the
-- trigger's value in place, same as the seed).
-- Run it after setup; every section prints PASS or FAIL.
-- =============================================================================
USE ERPPRD01;
GO
SET NOCOUNT ON;

PRINT '=== Exhibit 1: order-hold logic is split across 3 objects ===';

-- 1a. Order 1042 is on credit hold
IF EXISTS (SELECT 1 FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = 1042 AND STS_FLG = 'H' AND HLD_RSN_CD = 'CR')
    PRINT 'PASS  1a: order 1042 is on hold with reason CR';
ELSE PRINT 'FAIL  1a: order 1042 is not in the expected hold state';

-- 1b. The trigger-school exposure (N,H only) exceeds the credit limit -> the hold is justified
DECLARE @expoTrig NUMERIC(15,2), @lmt NUMERIC(15,2);
SELECT @expoTrig = ISNULL(SUM(ORD_TOT_AMT),0) FROM dbo.OE_ORD_HDR WHERE CUST_ACCT_ID = 100 AND STS_FLG IN ('N','H');
SELECT @lmt = CR_LMT_AMT FROM dbo.AR_CUST_ACCT WHERE CUST_ACCT_ID = 100;
IF @expoTrig > @lmt
    PRINT 'PASS  1b: trigger-style exposure ' + CAST(@expoTrig AS VARCHAR) + ' exceeds limit ' + CAST(@lmt AS VARCHAR);
ELSE PRINT 'FAIL  1b: exposure does not exceed limit, hold would be unjustified';

-- 1c. The hold threshold (100%) and the release threshold (110%) differ by
--     design: the system flags on its own, a human may override up to the
--     ceiling. Order 1042 sits inside that override band, so its release
--     should be a formality — which is what makes 1d interesting.
IF @expoTrig > @lmt AND @expoTrig <= @lmt * 1.10
    PRINT 'PASS  1c: trigger-style exposure ' + CAST(@expoTrig AS VARCHAR)
        + ' sits in the override band (held above ' + CAST(@lmt AS VARCHAR)
        + ', releasable up to ' + CAST(@lmt * 1.10 AS VARCHAR) + ')';
ELSE PRINT 'FAIL  1c: expected exposure between 100% and 110% of the credit limit';

-- 1d. SP_REL_ORD_HLD refuses the release anyway: it applies the 110% ceiling
--     to a different exposure figure (X-counting) than the one the hold used
BEGIN TRY
    EXEC dbo.SP_REL_ORD_HLD @ORD_HDR_ID = 1042, @USR_NM = 'VERIFY';
    PRINT 'FAIL  1d: release was accepted, expected a refusal';
END TRY
BEGIN CATCH
    PRINT 'PASS  1d: release refused -> ' + ERROR_MESSAGE();
END CATCH;

PRINT '';
PRINT '=== Exhibit 3 (drives 1d): the two schools of status X disagree ===';

DECLARE @expoProc NUMERIC(15,2);
EXEC dbo.SP_GET_CUST_EXPO @CUST_ACCT_ID = 100, @EXPO_AMT = @expoProc OUTPUT;
IF @expoProc > @expoTrig
    PRINT 'PASS  3a: SP_GET_CUST_EXPO says ' + CAST(@expoProc AS VARCHAR)
        + ' (X included) vs trigger-style ' + CAST(@expoTrig AS VARCHAR) + ' (X excluded)';
ELSE PRINT 'FAIL  3a: the two exposure calculations agree, they should not';

IF @expoTrig <= @lmt * 1.10 AND @expoProc > @lmt * 1.10
    PRINT 'PASS  3b: order 1042 would be releasable if X were excluded — the cancelled order 1051 is what keeps Rossi blocked';
ELSE PRINT 'FAIL  3b: expected the X-order to be the deciding factor in the refusal';

PRINT '';
PRINT '=== Exhibit 2: SP_GET_ITM_AVL only sees warehouse MAIN ===';

DECLARE @avl NUMERIC(15,3), @allWhse NUMERIC(15,3), @naive NUMERIC(15,3);
EXEC dbo.SP_GET_ITM_AVL @ITEM_CD = 'BRK-204', @AVL_QTY = @avl OUTPUT;
SELECT @allWhse = SUM(QTY_OH - QTY_COMM), @naive = SUM(QTY_OH)
  FROM dbo.INV_ONHAND_QTY oh JOIN dbo.INV_ITEM_MST im ON im.ITEM_ID = oh.ITEM_ID
 WHERE im.ITEM_CD = 'BRK-204';
IF @avl = 30 AND @allWhse = 55 AND @naive = 70
    PRINT 'PASS  2: proc says 30 (MAIN only), all-warehouse net is 55, naive SUM(QTY_OH) is 70 — three answers, one question';
ELSE PRINT 'FAIL  2: expected 30 / 55 / 70, got ' + CAST(@avl AS VARCHAR) + ' / ' + CAST(@allWhse AS VARCHAR) + ' / ' + CAST(@naive AS VARCHAR);

-- 2b. Oversold item: committed exceeds on hand, nothing objects
IF EXISTS (SELECT 1 FROM dbo.INV_ONHAND_QTY oh JOIN dbo.INV_ITEM_MST im ON im.ITEM_ID = oh.ITEM_ID
            WHERE im.ITEM_CD = 'BLT-M8-40' AND oh.QTY_COMM > oh.QTY_OH)
    PRINT 'PASS  2b: BLT-M8-40 is oversold (committed > on hand) and no constraint minds';
ELSE PRINT 'FAIL  2b: expected an oversold stock row for BLT-M8-40';

PRINT '';
PRINT '=== Exhibit 4: proc and trigger disagree on order totals ===';

DECLARE @before NUMERIC(15,2), @afterProc NUMERIC(15,2), @afterTrig NUMERIC(15,2);
SELECT @before = ORD_TOT_AMT FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = 1046;
EXEC dbo.SP_CALC_ORD_TOT @ORD_HDR_ID = 1046;
SELECT @afterProc = ORD_TOT_AMT FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = 1046;
UPDATE dbo.OE_ORD_LINE SET QTY_ORD = QTY_ORD WHERE ORD_HDR_ID = 1046;  -- no-op touch, fires the trigger
SELECT @afterTrig = ORD_TOT_AMT FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = 1046;
IF @before = 900.00 AND @afterProc = 855.00 AND @afterTrig = 900.00
    PRINT 'PASS  4: order 1046 total: stored 900.00 -> SP_CALC_ORD_TOT rewrites 855.00 -> touching a line flips it back to 900.00';
ELSE PRINT 'FAIL  4: expected 900 -> 855 -> 900, got ' + CAST(@before AS VARCHAR) + ' -> ' + CAST(@afterProc AS VARCHAR) + ' -> ' + CAST(@afterTrig AS VARCHAR);

-- 4b. Stale total: order 1009 matches NO current formula
DECLARE @stored NUMERIC(15,2), @lines NUMERIC(15,2);
SELECT @stored = ORD_TOT_AMT FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = 1009;
SELECT @lines = SUM(QTY_ORD * UNIT_PRC) FROM dbo.OE_ORD_LINE WHERE ORD_HDR_ID = 1009;
IF @stored = 1250.00 AND @lines = 1184.00
    PRINT 'PASS  4b: order 1009 stores 1250.00 but its lines compute 1184.00 under every known formula';
ELSE PRINT 'FAIL  4b: expected 1250.00 stored vs 1184.00 computed, got ' + CAST(@stored AS VARCHAR) + ' vs ' + CAST(@lines AS VARCHAR);

PRINT '';
PRINT '=== Data smells: duplicates, orphans, undocumented holds ===';

-- Duplicate customer: same TAX_REF, two accounts
IF (SELECT COUNT(*) FROM dbo.AR_CUST_ACCT WHERE TAX_REF = 'IT09876540019') = 2
    PRINT 'PASS  5a: Bianchi exists twice (accounts 101 and 102, same tax id)';
ELSE PRINT 'FAIL  5a: expected exactly 2 Bianchi accounts';

-- Orphan flavour 1: order 1077 points at a soft-deleted customer
IF EXISTS (SELECT 1 FROM dbo.OE_ORD_HDR o JOIN dbo.AR_CUST_ACCT c ON c.CUST_ACCT_ID = o.CUST_ACCT_ID
            WHERE o.ORD_HDR_ID = 1077 AND o.STS_FLG = 'N' AND c.STS_FLG = 'X')
    PRINT 'PASS  5b: order 1077 is open against soft-deleted customer 103';
ELSE PRINT 'FAIL  5b: expected order 1077 open against a soft-deleted customer';

-- Orphan flavour 2: order 1013 points at a customer that does not exist at all
IF EXISTS (SELECT 1 FROM dbo.OE_ORD_HDR o WHERE o.ORD_HDR_ID = 1013
              AND NOT EXISTS (SELECT 1 FROM dbo.AR_CUST_ACCT c WHERE c.CUST_ACCT_ID = o.CUST_ACCT_ID))
    PRINT 'PASS  5c: order 1013 references customer 99, which no longer exists (no FK ever objected)';
ELSE PRINT 'FAIL  5c: expected a hard orphan on order 1013';

-- Hold with no reason
IF EXISTS (SELECT 1 FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = 1017 AND STS_FLG = 'H' AND HLD_RSN_CD IS NULL)
    PRINT 'PASS  5d: order 1017 is on hold with no reason code (pre-2014 trigger vintage)';
ELSE PRINT 'FAIL  5d: expected order 1017 held with NULL reason';

-- Audit silence: 1042 was put on hold by the trigger, so no audit row exists
IF NOT EXISTS (SELECT 1 FROM dbo.FND_AUDIT_TRL WHERE OBJ_NM = 'OE_ORD_HDR' AND OBJ_ID = 1042)
    PRINT 'PASS  5e: no audit row for the 1042 hold — triggers never write the audit trail';
ELSE PRINT 'FAIL  5e: unexpected audit row for order 1042';

PRINT '';
PRINT '99_verify.sql completed.';
GO
