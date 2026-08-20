-- =============================================================================
-- 02_constraints.sql — CHECK constraints
--
-- This is piece 1 of 3 of the order-hold logic (see tour-of-the-mess.md #1):
-- the constraint validates STATUS CODES, the trigger decides WHO goes on hold,
-- the stored procedure decides who comes OFF hold. No single place tells the
-- whole story.
-- =============================================================================
USE ERPPRD01;
GO

-- Valid order status codes. Note that 'X' is a legal value here — the
-- constraint has no opinion on what it MEANS (nobody agrees on that anyway).
ALTER TABLE dbo.OE_ORD_HDR ADD CONSTRAINT CK_OE_HDR_STS
    CHECK (STS_FLG IN ('N','H','P','S','X'));
GO

ALTER TABLE dbo.OE_ORD_LINE ADD CONSTRAINT CK_OE_LINE_STS
    CHECK (STS_FLG IN ('N','S','X'));
GO

ALTER TABLE dbo.AR_CUST_ACCT ADD CONSTRAINT CK_AR_CUST_STS
    CHECK (STS_FLG IN ('A','H','X'));
GO

-- Quantities and amounts must be non-negative. This is as far as declarative
-- integrity goes in this system.
ALTER TABLE dbo.OE_ORD_LINE ADD CONSTRAINT CK_OE_LINE_QTY CHECK (QTY_ORD > 0);
ALTER TABLE dbo.OE_ORD_LINE ADD CONSTRAINT CK_OE_LINE_PRC CHECK (UNIT_PRC >= 0);
ALTER TABLE dbo.OE_ORD_LINE ADD CONSTRAINT CK_OE_LINE_DSCPCT CHECK (LN_DSC_PCT BETWEEN 0 AND 100);
ALTER TABLE dbo.OE_ORD_HDR  ADD CONSTRAINT CK_OE_HDR_DSCPCT  CHECK (DSC_PCT BETWEEN 0 AND 100);
ALTER TABLE dbo.INV_ONHAND_QTY ADD CONSTRAINT CK_INV_OH_QTY  CHECK (QTY_OH >= 0);
ALTER TABLE dbo.INV_ONHAND_QTY ADD CONSTRAINT CK_INV_OH_COMM CHECK (QTY_COMM >= 0);
GO

-- What is deliberately NOT here (the gaps are part of the exhibit):
--   * no CHECK forcing HLD_RSN_CD to be populated when STS_FLG = 'H'
--     (holds without a reason exist in the data)
--   * no UNIQUE constraint on ACCT_NUM or on (PARTY_NAME) — duplicates exist
--   * no FK from OE_ORD_HDR.CUST_ACCT_ID to AR_CUST_ACCT — orphans exist

PRINT '02_constraints.sql completed.';
GO
