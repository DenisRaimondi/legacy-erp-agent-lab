-- =============================================================================
-- 05_seed.sql — hand-crafted sample data
--
-- Every quirk lives on specific, documented rows — see docs/tour-of-the-mess.md.
-- Triggers are DISABLED during seeding (as any DBA doing a data migration
-- would) so that final states are exactly what this script says, including the
-- inconsistent ones. IDs are explicit (IDENTITY_INSERT) so the tour can cite
-- them. ID gaps are intentional: rows lost in "the 2011 migration".
--
-- Key rows:
--   CUST 100  Rossi Impianti      — over credit limit, order 1042 on hold
--   CUST 101/102 Bianchi          — near-duplicate accounts
--   CUST 103  Nordwind (X)        — soft-deleted, but order 1077 still open
--   ORD 1042  the blocked order   | ORD 1051 cancelled-but-still-counted (X)
--   ORD 1046  discount drift demo | ORD 1009 stale total (matches no formula)
--   ORD 1017  hold with NULL reason (pre-2014 trigger)
--   ORD 1013  orphan (customer physically deleted in 2011 migration)
--   ORD 1058  the order users will ask to "delete"
--   ITEM 5000 BRK-204             — the availability trap (MAIN/SEC1/TRNS)
-- =============================================================================
USE ERPPRD01;
GO

DISABLE TRIGGER dbo.TRG_OE_ORD_HDR_AI  ON dbo.OE_ORD_HDR;
DISABLE TRIGGER dbo.TRG_OE_ORD_LINE_AIU ON dbo.OE_ORD_LINE;
GO

DELETE FROM dbo.FND_AUDIT_TRL;
DELETE FROM dbo.OE_ORD_LINE;
DELETE FROM dbo.OE_ORD_HDR;
DELETE FROM dbo.INV_ONHAND_QTY;
DELETE FROM dbo.INV_ITEM_MST;
DELETE FROM dbo.AR_CUST_ACCT;
GO

-- ---------------------------------------------------------------- customers --
SET IDENTITY_INSERT dbo.AR_CUST_ACCT ON;
INSERT INTO dbo.AR_CUST_ACCT
    (CUST_ACCT_ID, ACCT_NUM, PARTY_NAME, TAX_REF, ADDR_TXT, CTRY_CD,
     CR_LMT_AMT, PMT_TRM_CD, STS_FLG, CRTD_BY, CRTD_DT, LST_UPD_BY, LST_UPD_DT)
VALUES
-- migrated rows: CRTD_BY 'CONV', no last-update info. The 2011 vintage.
 (100,'C000100','Rossi Impianti S.p.A.','IT01234560017','Via Torino 41, Rivoli','IT',5000.00,'N30','A','CONV','2011-06-30',NULL,NULL)
,(101,'C000101','BIANCHI SRL','IT09876540019','V. Marconi 12, Cuneo','IT',15000.00,'N60','A','CONV','2011-06-30',NULL,NULL)
,(102,'C001102','Bianchi S.r.l.','IT09876540019','Via G. Marconi 12, Cuneo (CN)','IT',NULL,'N30','A','MROSSI','2019-03-12','MROSSI','2019-03-12')  -- same company, re-keyed in 2019
,(103,'C000103','Nordwind Logistik GmbH','DE811223344','Hafenstr. 8, Hamburg','DE',20000.00,'N30','X','CONV','2011-06-30','SA','2019-11-02')       -- soft-deleted 2019
,(104,'C000104','Ferretti Costruzioni S.r.l.','IT05566770016','C.so Francia 210, Torino','IT',25000.00,'RD30','A','CONV','2011-06-30',NULL,NULL)
,(105,'C000105','Iberica Suministros S.L.','ESB76543210','Calle Alcala 180, Madrid','ES',18000.00,'N60','A','CONV','2011-06-30',NULL,NULL)
,(106,'C000106','Ostrava Steel s.r.o.','CZ29876543','Vitkovicka 12, Ostrava','CZ',10000.00,'N30','A','CONV','2011-06-30',NULL,NULL)
,(107,'C000107','Marchand & Fils SARL','FR40123456789','12 Rue de la Gare, Lyon','FR',12000.00,'N45','A','CONV','2011-06-30','LBIANCHI','2024-05-20')
,(108,'C000108','Van Dijk Handel B.V.','NL861234567B01','Keizersgracht 44, Amsterdam','NL',30000.00,'N30','A','MROSSI','2015-02-09','MROSSI','2021-06-14')
,(109,'C000109','Alpenbau AG','CHE123456789','Bahnhofstrasse 3, Chur','CH',40000.00,'N30','A','MROSSI','2016-09-01',NULL,NULL)
,(110,'C000110','Kowalski Sp. z o.o.','PL5252525252','ul. Prosta 51, Warszawa','PL',8000.00,'N30','A','LBIANCHI','2018-01-25','SA','2022-03-03')
,(111,'C000111','Lindqvist Industri AB','SE556677889901','Industrigatan 7, Malmo','SE',22000.00,'N60','A','LBIANCHI','2018-07-19',NULL,NULL)
,(112,'C000112','Duarte Metalurgica Lda','PT509876543','Rua do Ouro 33, Porto','PT',9000.00,'N30','A','LBIANCHI','2020-10-08',NULL,NULL)
,(113,'C000113','Papadopoulos & Co O.E.','EL801234567','Leof. Kifisias 90, Athina','GR',6000.00,'N30','A','MROSSI','2021-04-22',NULL,NULL)
,(114,'C000114','Novak d.o.o.','SI12345678','Celovska cesta 25, Ljubljana','SI',7500.00,'N30','A','MROSSI','2022-11-30',NULL,NULL)
,(115,'C000115','Meridiana Impianti S.r.l.','IT07788990012','Via Etnea 95, Catania','IT',10000.00,'N30','H','LBIANCHI','2023-06-05','MGRECU','2026-01-18') -- customer-level admin hold
;
SET IDENTITY_INSERT dbo.AR_CUST_ACCT OFF;
GO

-- -------------------------------------------------------------------- items --
SET IDENTITY_INSERT dbo.INV_ITEM_MST ON;
INSERT INTO dbo.INV_ITEM_MST
    (ITEM_ID, ITEM_CD, ITEM_DESC, UOM_CD, LST_PRC, ITM_CLS_CD, STS_FLG, CRTD_BY, CRTD_DT, LST_UPD_BY, LST_UPD_DT)
VALUES
 (5000,'BRK-204','BRACKET STEEL 204MM','EA',12.50,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5001,'BLT-M8-40','BOLT HEX M8X40 8.8 ZN','EA',0.18,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5002,'BLT-M10-60','BOLT HEX M10X60 8.8 ZN','EA',0.35,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5003,'NUT-M8','NUT HEX M8 8 ZN','EA',0.05,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5004,'WSH-M8','WASHER FLAT M8 ZN','EA',0.02,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5005,'FLG-DN50','FLANGE PN16 DN50 S235','EA',8.90,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5006,'FLG-DN80','FLANGE PN16 DN80 S235','EA',14.20,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5007,'PMP-C12','PUMP CENTRIFUGAL C-12 1.5KW','EA',420.00,'FG','A','CONV','2011-06-30','MROSSI','2023-02-10')
,(5008,'VLV-BALL-2','VALVE BALL 2IN BRASS','EA',26.40,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5009,'VLV-GATE-3','VALVE GATE 3IN CAST IRON','EA',68.00,'FG','A','CONV','2011-06-30',NULL,NULL)
,(5010,'MTR-045','MOTOR 3PH 0.45KW B14','EA',96.00,'FG','A','MROSSI','2014-05-06',NULL,NULL)
,(5011,'MTR-075','MOTOR 3PH 0.75KW B14','EA',128.00,'FG','A','MROSSI','2014-05-06',NULL,NULL)
,(5012,'BRG-6204','BEARING 6204 2RS','EA',3.10,'SP','A','CONV','2011-06-30',NULL,NULL)
,(5013,'BRG-6305','BEARING 6305 ZZ','EA',5.80,'SP','A','CONV','2011-06-30',NULL,NULL)
,(5014,'SPR-COMP-50','SPRING COMPRESSION 50X10','EA',1.15,'SP','A','LBIANCHI','2017-09-13',NULL,NULL)
,(5015,'CHN-08B','CHAIN ROLLER 08B-1 5M','EA',22.30,'SP','A','LBIANCHI','2017-09-13',NULL,NULL)
,(5016,'GRB-STD','GEARBOX WORM 1:20 STD','EA',210.00,'FG','A','MROSSI','2015-03-27',NULL,NULL)
,(5017,'PLT-S235-3','PLATE S235 3MM 1000X2000','EA',38.00,'RM','A','CONV','2011-06-30',NULL,NULL)
,(5018,'PLT-S235-5','PLATE S235 5MM 1000X2000','EA',61.00,'RM','A','CONV','2011-06-30',NULL,NULL)
,(5019,'TUB-INOX-25','TUBE INOX 304 D25 6M','EA',29.50,'RM','A','CONV','2011-06-30',NULL,NULL)
,(5020,'TUB-INOX-40','TUBE INOX 304 D40 6M','EA',44.80,'RM','A','CONV','2011-06-30',NULL,NULL)
,(5021,'CBL-3G15','CABLE 3G1.5 FROR 100M','EA',52.00,'FG','A','MROSSI','2016-01-14',NULL,NULL)
,(5022,'CBL-5G25','CABLE 5G2.5 FROR 100M','EA',118.00,'FG','A','MROSSI','2016-01-14',NULL,NULL)
,(5023,'SNS-PRX-M12','SENSOR PROXIMITY M12 PNP','EA',18.60,'FG','A','LBIANCHI','2019-06-03',NULL,NULL)
,(5024,'SNS-TMP-K','SENSOR TEMP TYPE-K 2M','EA',11.90,'FG','A','LBIANCHI','2019-06-03',NULL,NULL)
,(5025,'GSK-DN50','GASKET DN50 KLINGERIT','EA',0.85,'SP','A','CONV','2011-06-30',NULL,NULL)
,(5026,'GSK-DN80','GASKET DN80 KLINGERIT','EA',1.30,'SP','A','CONV','2011-06-30',NULL,NULL)
,(5027,'LUB-EP2','GREASE LITHIUM EP2 400G','EA',4.60,'SP','A','LBIANCHI','2018-02-20',NULL,NULL)
,(5028,'ZZZ-OBS-1','*** DO NOT USE *** OLD CODE','EA',NULL,'ZZ','A','CONV','2011-06-30','SA','2013-04-01') -- class ZZ, meaning lost
,(5029,'FRT-CHG','FREIGHT CHARGE (PSEUDO-ITEM)','EA',NULL,'ZZ','A','CONV','2011-06-30',NULL,NULL)          -- not a product, used on order lines anyway
;
SET IDENTITY_INSERT dbo.INV_ITEM_MST OFF;
GO

-- -------------------------------------------------------------------- stock --
-- BRK-204 is the availability exhibit:
--   MAIN 45 on hand, 15 committed -> proc says 30
--   SEC1  5 on hand (invisible to the proc)
--   TRNS 20 arriving (invisible to the proc)
-- Committed 15 in MAIN = order 1030 (qty 5, Rossi) + order 1058 (qty 10, Van Dijk)
-- BLT-M8-40 is oversold: committed exceeds on hand. No constraint minds.
INSERT INTO dbo.INV_ONHAND_QTY (ITEM_ID, WHSE_CD, QTY_OH, QTY_COMM, LST_UPD_BY, LST_UPD_DT) VALUES
 (5000,'MAIN', 45, 15,'WHSMGR','2026-08-14')
,(5000,'SEC1',  5,  0,'WHSMGR','2026-07-30')
,(5000,'TRNS', 20,  0,'WHSMGR','2026-08-17')
,(5001,'MAIN',100,120,'WHSMGR','2026-08-10')   -- oversold
,(5002,'MAIN',540, 60,'WHSMGR','2026-08-10')
,(5003,'MAIN',2500,0,'WHSMGR','2026-06-02')
,(5005,'MAIN', 80, 12,'WHSMGR','2026-08-01')
,(5006,'MAIN', 35,  0,'WHSMGR','2026-08-01')
,(5007,'MAIN',  4,  1,'WHSMGR','2026-08-12')
,(5008,'MAIN', 60,  6,'WHSMGR','2026-07-22')
,(5009,'QC01',  8,  0,'WHSMGR','2026-08-18')   -- stuck in quality check
,(5012,'MAIN',220,  0,'WHSMGR','2026-05-15')
,(5016,'MAIN',  6,  2,'WHSMGR','2026-08-05')
,(5017,'SEC1', 40,  0,'WHSMGR','2026-04-11')
,(5023,'MAIN', 75, 10,'WHSMGR','2026-08-09')
;
GO

-- ------------------------------------------------------------------- orders --
-- Explicit IDs; gaps = the 2011 migration. Statuses are FINAL states.
SET IDENTITY_INSERT dbo.OE_ORD_HDR ON;
INSERT INTO dbo.OE_ORD_HDR
    (ORD_HDR_ID, ORD_NUM, CUST_ACCT_ID, ORD_DT, STS_FLG, HLD_RSN_CD, DSC_PCT, ORD_TOT_AMT, CRTD_BY, CRTD_DT, LST_UPD_BY, LST_UPD_DT)
VALUES
-- older shipped/posted history (filler, statuses S/P, totals plausible)
 (1001,'SO-2023-0012',104,'2023-01-19','S',NULL, 0.00, 3120.00,'CONV','2023-01-19',NULL,NULL)
,(1004,'SO-2023-0055',100,'2023-04-02','P',NULL, 0.00, 1800.00,'MROSSI','2023-04-02','MROSSI','2023-05-11')
,(1006,'SO-2023-0078',105,'2023-05-30','S',NULL, 2.00, 6444.00,'MROSSI','2023-05-30',NULL,NULL)
,(1009,'SO-2023-0104',106,'2023-07-21','P',NULL, 0.00, 1250.00,'CONV','2023-07-21',NULL,NULL)      -- STALE total: no current formula reproduces it
,(1011,'SO-2023-0131',108,'2023-09-14','S',NULL, 0.00, 5230.40,'LBIANCHI','2023-09-14',NULL,NULL)
,(1013,'SO-2023-0142',99 ,'2023-10-06','S',NULL, 0.00,  980.00,'CONV','2023-10-06',NULL,NULL)      -- ORPHAN: customer 99 physically deleted in 2011 migration
,(1017,'SO-2013-0801',106,'2013-11-25','H',NULL, 0.00, 4200.00,'CONV','2013-11-25',NULL,NULL)      -- hold with NULL reason (pre-2014 trigger)
,(1021,'SO-2026-0009',101,'2026-02-11','S',NULL, 0.00, 3400.00,'LBIANCHI','2026-02-11',NULL,NULL)  -- BIANCHI SRL
,(1024,'SO-2024-0033',109,'2024-03-08','S',NULL, 5.00,12160.00,'MROSSI','2024-03-08',NULL,NULL)
,(1027,'SO-2024-0060',111,'2024-05-17','S',NULL, 0.00, 2648.00,'MROSSI','2024-05-17',NULL,NULL)
,(1030,'SO-2026-0021',100,'2026-03-05','N',NULL, 0.00, 2582.50,'MROSSI','2026-03-05',NULL,NULL)    -- Rossi open order (part of exposure)
,(1033,'SO-2024-0102',112,'2024-08-22','S',NULL, 0.00,  846.00,'LBIANCHI','2024-08-22',NULL,NULL)
,(1036,'SO-2024-0140',107,'2024-11-03','P',NULL, 0.00, 1571.60,'LBIANCHI','2024-11-03',NULL,NULL)
,(1039,'SO-2025-0018',110,'2025-02-14','S',NULL, 0.00,  624.00,'MROSSI','2025-02-14',NULL,NULL)
,(1042,'SO-2026-0035',100,'2026-04-18','H','CR', 0.00, 2600.00,'MROSSI','2026-04-18','TRG_CR','2026-04-18') -- THE blocked order
,(1044,'SO-2026-0041',101,'2026-06-02','N',NULL, 0.00, 1498.40,'LBIANCHI','2026-06-02',NULL,NULL)  -- BIANCHI SRL open
,(1046,'SO-2026-0048',104,'2026-06-25','N',NULL, 5.00,  900.00,'MROSSI','2026-06-25','TRG_TOT','2026-06-25') -- discount-drift exhibit (trigger value stored)
,(1048,'SO-2025-0077',113,'2025-06-30','S',NULL, 0.00,  558.00,'MROSSI','2025-06-30',NULL,NULL)
,(1051,'SO-2026-0050',100,'2026-07-02','X',NULL, 0.00, 2228.00,'MROSSI','2026-07-02','LBIANCHI','2026-07-15') -- CANCELLED... but SP_GET_CUST_EXPO still counts it
,(1052,'SO-2026-0052',102,'2026-03-20','S',NULL, 0.00,  900.00,'MROSSI','2026-03-20',NULL,NULL)    -- Bianchi S.r.l. (the OTHER account)
,(1055,'SO-2025-0102',114,'2025-09-09','S',NULL, 0.00,  372.00,'LBIANCHI','2025-09-09',NULL,NULL)
,(1058,'SO-2026-0058',108,'2026-08-03','N',NULL, 0.00, 1490.00,'LBIANCHI','2026-08-03',NULL,NULL)  -- the order users ask to "delete"
,(1061,'SO-2025-0141',105,'2025-11-28','S',NULL, 0.00, 2360.00,'MROSSI','2025-11-28',NULL,NULL)
,(1064,'SO-2026-0002',109,'2026-01-09','P',NULL, 0.00, 4200.00,'MROSSI','2026-01-09',NULL,NULL)
,(1067,'SO-2026-0014',111,'2026-02-20','N',NULL, 0.00, 1179.00,'LBIANCHI','2026-02-20',NULL,NULL)
,(1070,'SO-2026-0026',112,'2026-03-31','S',NULL, 0.00,  538.00,'LBIANCHI','2026-03-31',NULL,NULL)
,(1071,'SO-2026-0028',115,'2026-04-04','H','MN', 0.00,  859.00,'MGRECU','2026-04-04','MGRECU','2026-04-04') -- manual hold (customer 115 is on admin hold)
,(1073,'SO-2026-0044',107,'2026-06-11','N',NULL, 0.00,  742.20,'MROSSI','2026-06-11',NULL,NULL)
,(1075,'SO-2026-0053',110,'2026-07-08','N',NULL, 0.00,  312.40,'LBIANCHI','2026-07-08',NULL,NULL)
,(1077,'SO-2026-0056',103,'2026-07-29','N',NULL, 0.00, 5616.00,'SA','2026-07-29',NULL,NULL)        -- ORPHAN: customer 103 soft-deleted in 2019
,(1078,'SO-2026-0060',113,'2026-08-12','N',NULL, 0.00,  230.00,'MROSSI','2026-08-12',NULL,NULL)
;
SET IDENTITY_INSERT dbo.OE_ORD_HDR OFF;
GO

-- -------------------------------------------------------------- order lines --
-- Lines are provided for the exhibit orders (the ones the tour cites) and for
-- recent open orders. Old shipped orders keep header totals only — nobody has
-- ever reconciled those, which is itself authentic.
SET IDENTITY_INSERT dbo.OE_ORD_LINE ON;
INSERT INTO dbo.OE_ORD_LINE
    (ORD_LINE_ID, ORD_HDR_ID, LINE_NUM, ITEM_ID, QTY_ORD, UNIT_PRC, LN_DSC_PCT, STS_FLG, CRTD_BY, CRTD_DT)
VALUES
-- 1042 Rossi, blocked: pumps + brackets
 (1,1042,1,5007, 5,420.00, 0.00,'N','MROSSI','2026-04-18')
,(2,1042,2,5000,40, 12.50, 0.00,'N','MROSSI','2026-04-18')
-- 1030 Rossi, open: gearbox + brackets (commits 5 BRK-204 in MAIN)
,(3,1030,1,5016,12,210.00, 0.00,'N','MROSSI','2026-03-05')
,(4,1030,2,5000, 5, 12.50, 0.00,'N','MROSSI','2026-03-05')
-- 1051 Rossi, cancelled (lines X)
,(5,1051,1,5009,25, 68.00, 0.00,'X','MROSSI','2026-07-02')
,(6,1051,2,5008,20, 26.40, 0.00,'X','MROSSI','2026-07-02')
-- 1046 Ferretti, the discount-drift exhibit: 10 x 100.00, line disc 10%, header disc 5%
--   trigger (max rule):      1000 * 0.90        = 900.00  <- currently stored
--   proc (compound rule):    1000 * 0.90 * 0.95 = 855.00
,(7,1046,1,5011,10,100.00,10.00,'N','MROSSI','2026-06-25')
-- 1009 Ostrava, stale-total exhibit: lines say 1184.00 under BOTH formulas, header says 1250.00
,(8,1009,1,5017,24, 38.00, 0.00,'N','CONV','2023-07-21')
,(9,1009,2,5025,320, 0.85, 0.00,'N','CONV','2023-07-21')
-- 1058 Van Dijk, the "please delete this" order (commits 10 BRK-204 in MAIN)
,(10,1058,1,5000,10, 12.50, 0.00,'N','LBIANCHI','2026-08-03')
,(11,1058,2,5022,10,118.00, 0.00,'N','LBIANCHI','2026-08-03')
,(12,1058,3,5029, 1,185.00, 0.00,'N','LBIANCHI','2026-08-03')   -- freight as a pseudo-item line
-- 1077 orphan order (soft-deleted customer), real lines
,(13,1077,1,5007,10,420.00, 0.00,'N','SA','2026-07-29')
,(14,1077,2,5022,12,118.00, 0.00,'N','SA','2026-07-29')
-- 1044 BIANCHI SRL open
,(15,1044,1,5023,50, 18.60, 0.00,'N','LBIANCHI','2026-06-02')
,(16,1044,2,5013,98,  5.80, 0.00,'N','LBIANCHI','2026-06-02')
-- 1067 Lindqvist open
,(17,1067,1,5021,10, 52.00, 0.00,'N','LBIANCHI','2026-02-20')
,(18,1067,2,5022, 5,118.00, 0.00,'N','LBIANCHI','2026-02-20')
,(19,1067,3,5027,15,  4.60, 0.00,'N','LBIANCHI','2026-02-20')
-- 1071 Meridiana manual hold
,(20,1071,1,5005,60,  8.90, 0.00,'N','MGRECU','2026-04-04')
,(21,1071,2,5026,250, 1.30, 0.00,'N','MGRECU','2026-04-04')
-- 1073 Marchand open
,(22,1073,1,5008,20, 26.40, 0.00,'N','MROSSI','2026-06-11')
,(23,1073,2,5024,18, 11.90, 0.00,'N','MROSSI','2026-06-11')
-- 1075 Kowalski open
,(24,1075,1,5012,80,  3.10, 0.00,'N','LBIANCHI','2026-07-08')
,(25,1075,2,5027,14,  4.60, 0.00,'N','LBIANCHI','2026-07-08')
-- 1078 Papadopoulos open
,(26,1078,1,5003,2000,0.05, 0.00,'N','MROSSI','2026-08-12')
,(27,1078,2,5004,2000,0.02, 0.00,'N','MROSSI','2026-08-12')
,(28,1078,3,5001,500, 0.18, 0.00,'N','MROSSI','2026-08-12')
;
SET IDENTITY_INSERT dbo.OE_ORD_LINE OFF;
GO

-- -------------------------------------------------------------- audit trail --
-- Only proc-driven changes ever land here. The gaps tell their own story:
-- order 1042 went on hold via trigger — no audit row exists for that.
INSERT INTO dbo.FND_AUDIT_TRL (OBJ_NM, OBJ_ID, ACTN_CD, ACTN_BY, ACTN_DT, RMK_TXT) VALUES
 ('OE_ORD_HDR',1017,'REL_HLD','MGRECU','2014-02-03','Order released from hold')      -- released in 2014... and someone re-held it by hand later, who knows
,('OE_ORD_HDR',1051,'CANC','LBIANCHI','2026-07-15','Order cancelled (soft delete)')
,('OE_ORD_HDR',1064,'REL_HLD','MGRECU','2026-01-12','Order released from hold')
;
GO

ENABLE TRIGGER dbo.TRG_OE_ORD_HDR_AI  ON dbo.OE_ORD_HDR;
ENABLE TRIGGER dbo.TRG_OE_ORD_LINE_AIU ON dbo.OE_ORD_LINE;
GO

PRINT '05_seed.sql completed.';
GO
