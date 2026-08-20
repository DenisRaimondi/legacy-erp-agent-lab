-- =============================================================================
-- 98_reset_demo.sql — put order 1058 back the way the seed left it
--
-- Not part of setup. Cancelling an order is the one demonstration that changes
-- the database: it soft-deletes the order, releases the stock it had committed
-- and writes an audit row. Running this afterwards lets the demonstration be
-- repeated without rebuilding the container.
--
-- Idempotent: it writes absolute seed values rather than reversing a delta, so
-- running it twice, or when nothing has changed, is harmless.
--
-- Deleting the audit rows is the one thing in this repository that violates the
-- system's own convention — nothing here ever deletes. It is acceptable only
-- because this script is a test fixture and not part of the fiction.
-- =============================================================================
USE ERPPRD01;
GO
SET NOCOUNT ON;

-- Triggers off: the line trigger would recompute ORD_TOT_AMT from its own
-- formula and quietly replace the seeded total, exactly as it does during
-- seeding. Same reason, same precaution.
ALTER TABLE dbo.OE_ORD_LINE DISABLE TRIGGER TRG_OE_ORD_LINE_AIU;
GO

UPDATE dbo.OE_ORD_LINE
   SET STS_FLG = 'N', LST_UPD_BY = NULL, LST_UPD_DT = NULL
 WHERE ORD_HDR_ID = 1058;

UPDATE dbo.OE_ORD_HDR
   SET STS_FLG = 'N', LST_UPD_BY = NULL, LST_UPD_DT = NULL, ORD_TOT_AMT = 1490.00
 WHERE ORD_HDR_ID = 1058;

-- BRK-204 in MAIN: 15 committed, of which 10 belong to order 1058 and 5 to
-- order 1030. Items 5022 and 5029 carry no stock rows at all — one is not
-- stocked, the other is a freight charge that is not a product.
UPDATE dbo.INV_ONHAND_QTY
   SET QTY_COMM = 15.000, LST_UPD_BY = 'WHSMGR'
 WHERE ITEM_ID = 5000 AND WHSE_CD = 'MAIN';

DELETE FROM dbo.FND_AUDIT_TRL
 WHERE OBJ_NM = 'OE_ORD_HDR' AND OBJ_ID = 1058;

GO
ALTER TABLE dbo.OE_ORD_LINE ENABLE TRIGGER TRG_OE_ORD_LINE_AIU;
GO

-- Prove it landed, so a failed reset is not discovered later as a failing trap
DECLARE @sts CHAR(1), @comm NUMERIC(15,3), @audit INT;
SELECT @sts  = STS_FLG  FROM dbo.OE_ORD_HDR WHERE ORD_HDR_ID = 1058;
SELECT @comm = QTY_COMM FROM dbo.INV_ONHAND_QTY WHERE ITEM_ID = 5000 AND WHSE_CD = 'MAIN';
SELECT @audit = COUNT(*) FROM dbo.FND_AUDIT_TRL WHERE OBJ_NM = 'OE_ORD_HDR' AND OBJ_ID = 1058;

IF @sts = 'N' AND @comm = 15.000 AND @audit = 0
    PRINT 'PASS  reset: order 1058 open again, BRK-204 committed back to 15, audit clear';
ELSE
    PRINT 'FAIL  reset: order 1058 is ' + @sts + ', BRK-204 committed '
        + CAST(@comm AS VARCHAR) + ', ' + CAST(@audit AS VARCHAR) + ' audit rows';
GO
