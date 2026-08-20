-- =============================================================================
-- 01_schema.sql — ERPPRD01 core tables
--
-- Naming follows the house convention inherited from "the old system" (2011
-- migration): <module>_<abbreviated entity>. Vowels are a luxury.
-- Modules: AR (receivables/customers), INV (inventory), OE (order entry),
--          FND (foundation/shared).
--
-- NOTE the deliberate absence of some foreign keys (see tour-of-the-mess.md):
-- OE_ORD_HDR.CUST_ACCT_ID has NO FK — orphan orders are possible and present.
-- =============================================================================

IF DB_ID('ERPPRD01') IS NULL
    CREATE DATABASE ERPPRD01;
GO
USE ERPPRD01;
GO

-- Drop in dependency order so the script is re-runnable
IF OBJECT_ID('dbo.OE_ORD_LINE')    IS NOT NULL DROP TABLE dbo.OE_ORD_LINE;
IF OBJECT_ID('dbo.OE_ORD_HDR')     IS NOT NULL DROP TABLE dbo.OE_ORD_HDR;
IF OBJECT_ID('dbo.INV_ONHAND_QTY') IS NOT NULL DROP TABLE dbo.INV_ONHAND_QTY;
IF OBJECT_ID('dbo.INV_ITEM_MST')   IS NOT NULL DROP TABLE dbo.INV_ITEM_MST;
IF OBJECT_ID('dbo.AR_CUST_ACCT')   IS NOT NULL DROP TABLE dbo.AR_CUST_ACCT;
IF OBJECT_ID('dbo.FND_AUDIT_TRL')  IS NOT NULL DROP TABLE dbo.FND_AUDIT_TRL;
GO

-- -----------------------------------------------------------------------------
-- AR_CUST_ACCT — customer accounts
-- STS_FLG: 'A' = active, 'H' = admin hold, 'X' = deleted (soft delete: rows are
--          never physically removed; every reader is supposed to filter X out.
--          Not all of them do.)
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.AR_CUST_ACCT (
    CUST_ACCT_ID  INT IDENTITY(100,1) NOT NULL,
    ACCT_NUM      VARCHAR(30)   NOT NULL,
    PARTY_NAME    VARCHAR(240)  NOT NULL,
    TAX_REF       VARCHAR(20)   NULL,
    ADDR_TXT      VARCHAR(240)  NULL,
    CTRY_CD       CHAR(2)       NULL,
    CR_LMT_AMT    NUMERIC(15,2) NULL,          -- NULL means... unlimited? zero? depends who you ask
    PMT_TRM_CD    VARCHAR(4)    NULL,          -- 'N30','N60','RD30' — no reference table exists
    STS_FLG       CHAR(1)       NOT NULL CONSTRAINT DF_AR_CUST_STS DEFAULT 'A',
    CRTD_BY       VARCHAR(30)   NULL,
    CRTD_DT       DATETIME      NULL,
    LST_UPD_BY    VARCHAR(30)   NULL,
    LST_UPD_DT    DATETIME      NULL,
    CONSTRAINT PK_AR_CUST_ACCT PRIMARY KEY (CUST_ACCT_ID)
);
GO

-- -----------------------------------------------------------------------------
-- INV_ITEM_MST — item master
-- ITM_CLS_CD: 'FG' finished good, 'RM' raw material, 'SP' spare part,
--             'ZZ' — appears in the data, meaning unknown, do not remove
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.INV_ITEM_MST (
    ITEM_ID     INT IDENTITY(5000,1) NOT NULL,
    ITEM_CD     VARCHAR(30)   NOT NULL,
    ITEM_DESC   VARCHAR(240)  NULL,
    UOM_CD      VARCHAR(3)    NOT NULL CONSTRAINT DF_INV_ITEM_UOM DEFAULT 'EA',
    LST_PRC     NUMERIC(15,2) NULL,
    ITM_CLS_CD  VARCHAR(4)    NULL,
    STS_FLG     CHAR(1)       NOT NULL CONSTRAINT DF_INV_ITEM_STS DEFAULT 'A',
    CRTD_BY     VARCHAR(30)   NULL,
    CRTD_DT     DATETIME      NULL,
    LST_UPD_BY  VARCHAR(30)   NULL,
    LST_UPD_DT  DATETIME      NULL,
    CONSTRAINT PK_INV_ITEM_MST PRIMARY KEY (ITEM_ID)
);
GO

-- -----------------------------------------------------------------------------
-- INV_ONHAND_QTY — stock by warehouse
-- WHSE_CD: 'MAIN' central warehouse, 'SEC1' secondary, 'TRNS' goods in transit,
--          'QC01' quarantine/quality check
-- QTY_COMM = quantity committed to open sales orders
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.INV_ONHAND_QTY (
    ONHAND_ID  INT IDENTITY(1,1) NOT NULL,
    ITEM_ID    INT           NOT NULL,
    WHSE_CD    VARCHAR(4)    NOT NULL,
    QTY_OH     NUMERIC(15,3) NOT NULL CONSTRAINT DF_INV_OH_QTY DEFAULT 0,
    QTY_COMM   NUMERIC(15,3) NOT NULL CONSTRAINT DF_INV_OH_COMM DEFAULT 0,
    LST_UPD_BY VARCHAR(30)   NULL,
    LST_UPD_DT DATETIME      NULL,
    CONSTRAINT PK_INV_ONHAND_QTY PRIMARY KEY (ONHAND_ID),
    CONSTRAINT FK_INV_OH_ITEM FOREIGN KEY (ITEM_ID) REFERENCES dbo.INV_ITEM_MST (ITEM_ID)
);
GO

-- -----------------------------------------------------------------------------
-- OE_ORD_HDR — sales order headers
-- STS_FLG: 'N' new/open, 'H' on hold, 'P' posted, 'S' shipped, 'X' — see
--          tour-of-the-mess.md, this one is contested territory.
-- HLD_RSN_CD: 'CR' credit, 'MN' manual — only populated since ~2014, older
--          holds have NULL reason.
-- ORD_TOT_AMT is DENORMALIZED and maintained by two competing pieces of code.
-- NO foreign key to AR_CUST_ACCT (added to the backlog in 2013, still there).
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.OE_ORD_HDR (
    ORD_HDR_ID   INT IDENTITY(1000,1) NOT NULL,
    ORD_NUM      VARCHAR(20)   NOT NULL,
    CUST_ACCT_ID INT           NOT NULL,        -- no FK, orphans exist
    ORD_DT       DATETIME      NOT NULL,
    STS_FLG      CHAR(1)       NOT NULL CONSTRAINT DF_OE_HDR_STS DEFAULT 'N',
    HLD_RSN_CD   VARCHAR(4)    NULL,
    DSC_PCT      NUMERIC(5,2)  NOT NULL CONSTRAINT DF_OE_HDR_DSC DEFAULT 0,
    ORD_TOT_AMT  NUMERIC(15,2) NULL,
    CRTD_BY      VARCHAR(30)   NULL,
    CRTD_DT      DATETIME      NULL,
    LST_UPD_BY   VARCHAR(30)   NULL,
    LST_UPD_DT   DATETIME      NULL,
    CONSTRAINT PK_OE_ORD_HDR PRIMARY KEY (ORD_HDR_ID)
);
GO

-- -----------------------------------------------------------------------------
-- OE_ORD_LINE — sales order lines
-- LN_DSC_PCT: line-level discount. How it combines with the header DSC_PCT is
--             answered differently by SP_CALC_ORD_TOT and TRG_OE_ORD_LINE_AIU.
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.OE_ORD_LINE (
    ORD_LINE_ID INT IDENTITY(1,1) NOT NULL,
    ORD_HDR_ID  INT           NOT NULL,
    LINE_NUM    INT           NOT NULL,
    ITEM_ID     INT           NOT NULL,
    QTY_ORD     NUMERIC(15,3) NOT NULL,
    UNIT_PRC    NUMERIC(15,2) NOT NULL,
    LN_DSC_PCT  NUMERIC(5,2)  NOT NULL CONSTRAINT DF_OE_LINE_DSC DEFAULT 0,
    STS_FLG     CHAR(1)       NOT NULL CONSTRAINT DF_OE_LINE_STS DEFAULT 'N',
    CRTD_BY     VARCHAR(30)   NULL,
    CRTD_DT     DATETIME      NULL,
    LST_UPD_BY  VARCHAR(30)   NULL,
    LST_UPD_DT  DATETIME      NULL,
    CONSTRAINT PK_OE_ORD_LINE PRIMARY KEY (ORD_LINE_ID),
    CONSTRAINT FK_OE_LINE_HDR  FOREIGN KEY (ORD_HDR_ID) REFERENCES dbo.OE_ORD_HDR (ORD_HDR_ID),
    CONSTRAINT FK_OE_LINE_ITEM FOREIGN KEY (ITEM_ID)    REFERENCES dbo.INV_ITEM_MST (ITEM_ID)
);
GO

-- -----------------------------------------------------------------------------
-- FND_AUDIT_TRL — audit trail
-- Written by SOME of the code paths (the stored procedures). The triggers
-- predate it and were never retrofitted. Absence of an audit row proves nothing.
-- -----------------------------------------------------------------------------
CREATE TABLE dbo.FND_AUDIT_TRL (
    AUDIT_ID  INT IDENTITY(1,1) NOT NULL,
    OBJ_NM    VARCHAR(60)  NOT NULL,
    OBJ_ID    INT          NULL,
    ACTN_CD   VARCHAR(10)  NOT NULL,
    ACTN_BY   VARCHAR(30)  NOT NULL,
    ACTN_DT   DATETIME     NOT NULL CONSTRAINT DF_FND_AUD_DT DEFAULT GETDATE(),
    RMK_TXT   VARCHAR(400) NULL,
    CONSTRAINT PK_FND_AUDIT_TRL PRIMARY KEY (AUDIT_ID)
);
GO

PRINT '01_schema.sql completed.';
GO
