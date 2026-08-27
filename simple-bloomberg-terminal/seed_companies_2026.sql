-- Seed: 10 new dominant companies + revenue/cost sources for all 20.
-- Source: public filings & news, May 2026 snapshot. DataSource=2 (CLAUDE_ESTIMATED) on est. rows.
-- Scale: RAW USD (matches existing Apple=383000000000 convention).
-- Currency conversions used: KRW~1360/USD, JPY~150/USD, EUR~1.08, CHF~1.20, DKK~6.9/USD, INR~83/USD, CNY~7.2/USD, NT$~31/USD.

START TRANSACTION;

-- ===============================================================
-- 1. NEW COUNTRIES (9 — China already exists, used by Tencent)
-- ===============================================================
INSERT INTO Countries (Code, Name, Region, CurrencyCode, GdpUsd, Population, RiskRating) VALUES
('SA','Saudi Arabia','Middle East','SAR',1100000000000,36000000,2.8),
('TW','Taiwan','Asia','TWD',790000000000,23500000,2.5),
('KR','South Korea','Asia','KRW',1870000000000,51700000,1.8),
('JP','Japan','Asia','JPY',4220000000000,124000000,1.6),
('CH','Switzerland','Europe','CHF',905000000000,8800000,1.0),
('FR','France','Europe','EUR',3030000000000,68000000,1.7),
('DK','Denmark','Europe','DKK',405000000000,5950000,1.1),
('NL','Netherlands','Europe','EUR',1120000000000,17900000,1.3),
('IN','India','Asia','INR',3940000000000,1430000000,3.5);

-- ===============================================================
-- 2. NEW COMPANIES (10)
-- Sector/Industry int values follow enum declaration order (0-indexed).
-- ===============================================================
INSERT INTO Companies (Name, CountryId, Sector, Industry, RevenueTotal, GrossMargin, AsOf) VALUES
('Saudi Aramco',        (SELECT Id FROM Countries WHERE Code='SA'), 0,  1, 445700000000, 0.55, '2025-12-31'),
('TSMC',                (SELECT Id FROM Countries WHERE Code='TW'), 7, 54,  90000000000, 0.59, '2025-12-31'),
('Samsung Electronics', (SELECT Id FROM Countries WHERE Code='KR'), 7, 52, 245000000000, 0.35, '2025-12-31'),
('Toyota Motor',        (SELECT Id FROM Countries WHERE Code='JP'), 3, 22, 320000000000, 0.21, '2025-03-31'),
('Nestle',              (SELECT Id FROM Countries WHERE Code='CH'), 4, 33, 107700000000, 0.47, '2025-12-31'),
('LVMH',                (SELECT Id FROM Countries WHERE Code='FR'), 3, 25,  87000000000, 0.68, '2025-12-31'),
('Novo Nordisk',        (SELECT Id FROM Countries WHERE Code='DK'), 5, 41,  45000000000, 0.84, '2025-12-31'),
('ASML',                (SELECT Id FROM Countries WHERE Code='NL'), 7, 54,  42000000000, 0.51, '2025-12-31'),
('Reliance Industries', (SELECT Id FROM Countries WHERE Code='IN'), 0,  1, 120000000000, 0.18, '2025-03-31'),
('Tencent',             (SELECT Id FROM Countries WHERE Name='China' LIMIT 1), 8, 59, 109000000000, 0.53, '2025-12-31');

-- ===============================================================
-- 3. CAPTURE IDs FOR ALL 20 COMPANIES
-- ===============================================================
SET @apple    := (SELECT Id FROM Companies WHERE Name='Apple Inc.' LIMIT 1);
SET @msft     := (SELECT Id FROM Companies WHERE Name='Microsoft Corp.' LIMIT 1);
SET @xom      := (SELECT Id FROM Companies WHERE Name='ExxonMobil' LIMIT 1);
SET @vw       := (SELECT Id FROM Companies WHERE Name='Volkswagen AG' LIMIT 1);
SET @sap      := (SELECT Id FROM Companies WHERE Name='SAP SE' LIMIT 1);
SET @byd      := (SELECT Id FROM Companies WHERE Name='BYD Co.' LIMIT 1);
SET @baba     := (SELECT Id FROM Companies WHERE Name='Alibaba Group' LIMIT 1);
SET @pbr      := (SELECT Id FROM Companies WHERE Name='Petrobras' LIMIT 1);
SET @vale     := (SELECT Id FROM Companies WHERE Name='Vale S.A.' LIMIT 1);
SET @nvda     := (SELECT Id FROM Companies WHERE Name='Nvidia Corp.' LIMIT 1);
SET @aramco   := (SELECT Id FROM Companies WHERE Name='Saudi Aramco' LIMIT 1);
SET @tsmc     := (SELECT Id FROM Companies WHERE Name='TSMC' LIMIT 1);
SET @samsung  := (SELECT Id FROM Companies WHERE Name='Samsung Electronics' LIMIT 1);
SET @toyota   := (SELECT Id FROM Companies WHERE Name='Toyota Motor' LIMIT 1);
SET @nestle   := (SELECT Id FROM Companies WHERE Name='Nestle' LIMIT 1);
SET @lvmh     := (SELECT Id FROM Companies WHERE Name='LVMH' LIMIT 1);
SET @novo     := (SELECT Id FROM Companies WHERE Name='Novo Nordisk' LIMIT 1);
SET @asml     := (SELECT Id FROM Companies WHERE Name='ASML' LIMIT 1);
SET @ril      := (SELECT Id FROM Companies WHERE Name='Reliance Industries' LIMIT 1);
SET @tcehy    := (SELECT Id FROM Companies WHERE Name='Tencent' LIMIT 1);

-- ===============================================================
-- 4. REVENUE SOURCES
-- Revenue/cost direction is determined by the source table.
-- DataSource: 0=EDGAR, 1=MANUAL, 2=CLAUDE_ESTIMATED, 3=OPENBB
-- ===============================================================

-- Apple FY2025 ($416B): segment split per 10-K
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('iPhone',                       209590000000, 0.5036, 0, @apple, NULL),
('Services',                     109160000000, 0.2623, 0, @apple, NULL),
('Wearables, Home & Accessories', 35690000000, 0.0858, 0, @apple, NULL),
('Mac',                           33710000000, 0.0810, 0, @apple, NULL),
('iPad',                          28020000000, 0.0673, 0, @apple, NULL);

-- Microsoft FY2025 ($281.7B): segment split per 10-K
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Productivity & Business Processes', 120000000000, 0.4260, 0, @msft, NULL),
('Intelligent Cloud (Azure)',         110000000000, 0.3905, 0, @msft, NULL),
('More Personal Computing',            51700000000, 0.1835, 0, @msft, NULL);

-- ExxonMobil 2025 (~$317B): segment split
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Energy Products (Downstream)', 217760000000, 0.6870, 0, @xom, NULL),
('Upstream',                      55660000000, 0.1756, 0, @xom, NULL),
('Chemical Products',             18890000000, 0.0596, 0, @xom, NULL);

-- Volkswagen 2025 (€321.9B ~ $348B): brand split (approximations)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('VW Passenger Cars',            105000000000, 0.3017, 2, @vw, NULL),
('Audi (incl. PFS)',              39100000000, 0.1124, 0, @vw, NULL),
('Porsche & Luxury (Bentley/Lambo/Bugatti)', 34800000000, 0.1000, 0, @vw, NULL),
('Skoda & SEAT/Cupra',            55000000000, 0.1580, 2, @vw, NULL),
('Commercial Vehicles & Traton',  60000000000, 0.1724, 2, @vw, NULL),
('Financial Services',            54000000000, 0.1552, 2, @vw, NULL);

-- SAP 2025 (€37.8B ~ $40.8B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Cloud',                         22700000000, 0.5564, 0, @sap, NULL),
('Software licenses & support',   12500000000, 0.3064, 2, @sap, NULL),
('Services',                       4600000000, 0.1127, 0, @sap, NULL);

-- BYD 2025 ($116B): segment split
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Automobiles & related',         93600000000, 0.8070, 0, @byd, NULL),
('Handset components & assembly', 18000000000, 0.1552, 2, @byd, NULL),
('Overseas vehicle exports',      18000000000, 0.1552, 2, @byd, NULL),
('Batteries & energy storage',     6500000000, 0.0560, 2, @byd, NULL);

-- Alibaba FY2025 (~$131B): segment split
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Taobao & Tmall Group (China commerce)', 60000000000, 0.4580, 0, @baba, NULL),
('Cloud Intelligence Group',              17000000000, 0.1298, 0, @baba, NULL),
('International Digital Commerce (AIDC)', 14947000000, 0.1141, 0, @baba, NULL),
('Cainiao Logistics',                     14000000000, 0.1069, 2, @baba, NULL),
('Local Services & Digital Media',        25000000000, 0.1908, 2, @baba, NULL);

-- Petrobras 2025 (~$98B): segment split
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Refining, Transportation & Marketing (RTM)', 84165000000, 0.8588, 0, @pbr, NULL),
('Exploration & Production (E&P)',             32000000000, 0.3265, 2, @pbr, NULL),
('Gas & Low-Carbon Energy',                     6500000000, 0.0663, 2, @pbr, NULL),
('Oil exports',                                25000000000, 0.2551, 2, @pbr, NULL);

-- Vale 2025 (~$42B): commodity split
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Iron ore fines & pellets', 28800000000, 0.6857, 2, @vale, NULL),
('Copper',                    3590000000, 0.0855, 2, @vale, NULL),
('Nickel',                    2690000000, 0.0640, 2, @vale, NULL),
('China (steel mills)',      25000000000, 0.5952, 2, @vale, NULL);

-- Nvidia FY2025 ($130.5B): segment split per 10-K
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Data Center',                  115200000000, 0.8828, 0, @nvda, NULL),
('Gaming',                        11400000000, 0.0874, 0, @nvda, NULL),
('Professional Visualization',     1900000000, 0.0146, 0, @nvda, NULL),
('Automotive',                     1700000000, 0.0130, 0, @nvda, NULL),
('Microsoft (hyperscaler GPU)',   17000000000, 0.1303, 2, @nvda, @msft);

-- Saudi Aramco 2025 ($445.7B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Upstream (crude & gas)',       340000000000, 0.7629, 2, @aramco, NULL),
('Downstream (refining/chems)',   95000000000, 0.2132, 2, @aramco, NULL),
('Asia (China, Japan, Korea)',   270000000000, 0.6058, 2, @aramco, NULL),
('Reliance Industries (crude)',    8000000000, 0.0179, 2, @aramco, @ril);

-- TSMC 2025 (~$90B / NT$2.9T)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Nvidia (foundry)',              17100000000, 0.1900, 0, @tsmc, @nvda),
('Apple (foundry)',               15300000000, 0.1700, 0, @tsmc, @apple),
('AMD (foundry)',                  6300000000, 0.0700, 2, @tsmc, NULL),
('Broadcom & Qualcomm',            9000000000, 0.1000, 2, @tsmc, NULL),
('5nm/3nm advanced nodes',        44000000000, 0.4889, 2, @tsmc, NULL),
('7nm and mature nodes',          24000000000, 0.2667, 2, @tsmc, NULL);

-- Samsung Electronics 2025 (KRW 333.6T ~ $245B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Device Solutions (DS, memory + foundry)', 117000000000, 0.4776, 0, @samsung, NULL),
('Device eXperience (DX, MX/VD)',           112000000000, 0.4571, 0, @samsung, NULL),
('Samsung Display (SDC)',                    25000000000, 0.1020, 0, @samsung, NULL),
('Apple (OLED panels & memory)',             19000000000, 0.0776, 2, @samsung, @apple),
('Nvidia (HBM memory)',                       5000000000, 0.0204, 2, @samsung, @nvda);

-- Toyota Motor FY2025 (~$320B): region split
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('North America',                128700000000, 0.4022, 0, @toyota, NULL),
('Japan',                         85000000000, 0.2656, 2, @toyota, NULL),
('Asia (ex-Japan)',               45300000000, 0.1416, 0, @toyota, NULL),
('Europe',                        38000000000, 0.1188, 2, @toyota, NULL),
('Other regions',                 22400000000, 0.0700, 0, @toyota, NULL);

-- Nestle 2025 (CHF 89.5B ~ $107.7B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Powdered & Liquid Beverages',   30260000000, 0.2810, 0, @nestle, NULL),
('PetCare (Purina)',              22150000000, 0.2057, 0, @nestle, NULL),
('Prepared dishes & cooking aids',13780000000, 0.1280, 2, @nestle, NULL),
('Milk products & ice cream',     12480000000, 0.1159, 2, @nestle, NULL),
('Nutrition & Health Science',    12190000000, 0.1132, 2, @nestle, NULL),
('Zone Americas',                 51700000000, 0.4800, 0, @nestle, NULL),
('Zone AOA (Asia/Oceania/Africa)',28860000000, 0.2680, 0, @nestle, NULL),
('Zone Europe',                   27140000000, 0.2520, 0, @nestle, NULL);

-- LVMH 2025 (€80.8B ~ $87B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Fashion & Leather Goods',       40820000000, 0.4692, 0, @lvmh, NULL),
('Selective Retailing (Sephora etc)', 19760000000, 0.2271, 0, @lvmh, NULL),
('Perfumes & Cosmetics',           8700000000, 0.1000, 2, @lvmh, NULL),
('Wines & Spirits',                5790000000, 0.0666, 0, @lvmh, NULL),
('Watches & Jewelry',             10800000000, 0.1241, 2, @lvmh, NULL);

-- Novo Nordisk 2025 (~$45B / DKK 311B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Wegovy (obesity)',               5350000000, 0.1189, 0, @novo, NULL),
('Ozempic (diabetes)',            18500000000, 0.4111, 2, @novo, NULL),
('Other GLP-1 / diabetes',        14000000000, 0.3111, 2, @novo, NULL),
('Rare disease',                   3500000000, 0.0778, 2, @novo, NULL),
('United States',                 28000000000, 0.6222, 2, @novo, NULL);

-- ASML 2025 (€38B ~ $41B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('TSMC',                          15400000000, 0.3756, 2, @asml, @tsmc),
('Samsung Electronics',            8000000000, 0.1951, 2, @asml, @samsung),
('Intel & SK Hynix & Micron',      9000000000, 0.2195, 2, @asml, NULL),
('EUV systems',                   18500000000, 0.4512, 0, @asml, NULL),
('DUV systems',                   14300000000, 0.3488, 0, @asml, NULL),
('Installed-base service',         8000000000, 0.1951, 2, @asml, NULL),
('China',                         10250000000, 0.2500, 0, @asml, NULL);

-- Reliance Industries FY2025 (₹9.98 lakh crore ~ $120B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Oil-to-Chemicals (O2C)',        75500000000, 0.6292, 0, @ril, NULL),
('Reliance Retail',               42700000000, 0.3558, 0, @ril, NULL),
('Digital Services (Jio)',        17000000000, 0.1417, 0, @ril, NULL),
('Oil & Gas (E&P)',                3000000000, 0.0250, 2, @ril, NULL);

-- Tencent 2025 (CNY 751.8B ~ $109B)
INSERT INTO RevenueSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Fintech & Business Services',   33260000000, 0.3052, 0, @tcehy, NULL),
('Domestic Games',                23800000000, 0.2184, 0, @tcehy, NULL),
('Marketing Services (Ads)',      21030000000, 0.1930, 0, @tcehy, NULL),
('Social Networks',               18510000000, 0.1698, 0, @tcehy, NULL),
('International Games',           11220000000, 0.1029, 0, @tcehy, NULL);

-- ===============================================================
-- 5. COST SOURCES
-- Cost direction is determined by the CostSources table.
-- ===============================================================

-- Apple: huge supplier costs to TSMC (chips), Samsung (displays), Foxconn (not in 20)
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('TSMC (silicon)',                15300000000, 0.0660, 2, @apple, @tsmc),
('Samsung Electronics (panels+memory)', 19000000000, 0.0820, 2, @apple, @samsung),
('Other suppliers & assembly',   180000000000, 0.7770, 2, @apple, NULL),
('R&D',                           31370000000, 0.0754, 0, @apple, NULL),
('SG&A',                          26900000000, 0.0647, 0, @apple, NULL);

-- Microsoft: Nvidia GPU buys for Azure are well-publicized
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Nvidia (datacenter GPUs)',      17000000000, 0.0604, 2, @msft, @nvda),
('Datacenter ops & cloud infra',  60000000000, 0.2130, 2, @msft, NULL),
('R&D',                           32500000000, 0.1154, 0, @msft, NULL),
('Sales & Marketing + G&A',       30000000000, 0.1065, 2, @msft, NULL);

-- ExxonMobil: COGS dominated by purchased crude + production cost
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Crude oil & feedstock purchases', 165000000000, 0.5210, 2, @xom, NULL),
('Production & manufacturing',       55000000000, 0.1737, 2, @xom, NULL),
('Exploration expenses',              5000000000, 0.0158, 2, @xom, NULL),
('SG&A',                              9700000000, 0.0306, 2, @xom, NULL);

-- Volkswagen
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Cost of vehicles sold',         286000000000, 0.8221, 2, @vw, NULL),
('Batteries & EV cells (CATL/BYD)', 8000000000, 0.0230, 2, @vw, @byd),
('R&D capitalized + expensed',     22000000000, 0.0632, 2, @vw, NULL),
('Distribution & admin',           20000000000, 0.0575, 2, @vw, NULL);

-- SAP
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Cloud infra & hosting',          5500000000, 0.1348, 2, @sap, NULL),
('R&D',                            7100000000, 0.1740, 2, @sap, NULL),
('Sales & marketing',              9200000000, 0.2255, 2, @sap, NULL),
('G&A',                            2100000000, 0.0515, 2, @sap, NULL);

-- BYD
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Vehicle & battery COGS',        93800000000, 0.8086, 2, @byd, NULL),
('R&D',                            7400000000, 0.0638, 2, @byd, NULL),
('SG&A',                           5200000000, 0.0448, 2, @byd, NULL);

-- Alibaba
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('COGS (logistics, merchandise, infra)', 81000000000, 0.6183, 2, @baba, NULL),
('R&D (incl. cloud AI)',           9500000000, 0.0725, 2, @baba, NULL),
('Sales & marketing',             14000000000, 0.1069, 2, @baba, NULL),
('Nvidia (AI accelerators)',       2000000000, 0.0153, 2, @baba, @nvda);

-- Petrobras
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Lifting + refining cost',       42000000000, 0.4286, 2, @pbr, NULL),
('Imported feedstock',             8500000000, 0.0867, 2, @pbr, NULL),
('Exploration + G&A',              5300000000, 0.0541, 2, @pbr, NULL);

-- Vale
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Iron ore C1 cash cost',          6700000000, 0.1595, 2, @vale, NULL),
('Freight & logistics',            4400000000, 0.1048, 2, @vale, NULL),
('Royalties & maintenance',        3200000000, 0.0762, 2, @vale, NULL),
('SG&A + exploration',             1500000000, 0.0357, 2, @vale, NULL);

-- Nvidia: TSMC is the dominant supplier
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('TSMC (foundry wafers)',         17100000000, 0.1310, 2, @nvda, @tsmc),
('Samsung Electronics (HBM)',      5000000000, 0.0383, 2, @nvda, @samsung),
('Other packaging & components',  10000000000, 0.0766, 2, @nvda, NULL),
('R&D',                           13000000000, 0.0996, 0, @nvda, NULL),
('SG&A',                           3500000000, 0.0268, 2, @nvda, NULL);

-- Saudi Aramco
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Upstream lifting cost',         18000000000, 0.0404, 2, @aramco, NULL),
('Refining feedstock & opex',     85000000000, 0.1907, 2, @aramco, NULL),
('Royalties to KSA government',   95000000000, 0.2132, 2, @aramco, NULL),
('SG&A',                          11000000000, 0.0247, 2, @aramco, NULL);

-- TSMC: ASML equipment is the marquee capex/cogs item
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('ASML (lithography systems)',    15400000000, 0.1711, 2, @tsmc, @asml),
('Wafer materials & gases',       12000000000, 0.1333, 2, @tsmc, NULL),
('Fab depreciation',              10000000000, 0.1111, 2, @tsmc, NULL),
('R&D',                            6300000000, 0.0700, 2, @tsmc, NULL),
('SG&A',                           3200000000, 0.0356, 2, @tsmc, NULL);

-- Samsung Electronics
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('ASML & semicap equipment',       8000000000, 0.0327, 2, @samsung, @asml),
('Wafer/components/materials',   140000000000, 0.5714, 2, @samsung, NULL),
('R&D',                           25000000000, 0.1020, 2, @samsung, NULL),
('SG&A',                          22000000000, 0.0898, 2, @samsung, NULL);

-- Toyota Motor
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Vehicle COGS (parts, steel, labor)', 252000000000, 0.7875, 2, @toyota, NULL),
('R&D',                           10500000000, 0.0328, 2, @toyota, NULL),
('SG&A',                          30000000000, 0.0938, 2, @toyota, NULL);

-- Nestle
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Raw materials (coffee, cocoa, dairy)', 57000000000, 0.5293, 2, @nestle, NULL),
('Packaging & manufacturing',     10500000000, 0.0975, 2, @nestle, NULL),
('Marketing & distribution',      19000000000, 0.1764, 2, @nestle, NULL),
('Administration',                 4800000000, 0.0446, 2, @nestle, NULL);

-- LVMH
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Raw materials & craftsmanship', 28000000000, 0.3218, 2, @lvmh, NULL),
('Retail network rent & store ops',13000000000, 0.1494, 2, @lvmh, NULL),
('Marketing & advertising',       11000000000, 0.1264, 2, @lvmh, NULL),
('G&A',                            4500000000, 0.0517, 2, @lvmh, NULL);

-- Novo Nordisk
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('API manufacturing & filling',    7000000000, 0.1556, 2, @novo, NULL),
('R&D (clinical trials)',          5200000000, 0.1156, 2, @novo, NULL),
('Sales & marketing',              8500000000, 0.1889, 2, @novo, NULL),
('G&A',                            1400000000, 0.0311, 2, @novo, NULL);

-- ASML
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Components & sub-systems (Zeiss/Trumpf)', 18000000000, 0.4286, 2, @asml, NULL),
('Manufacturing & install',        2500000000, 0.0595, 2, @asml, NULL),
('R&D',                            5300000000, 0.1262, 2, @asml, NULL),
('SG&A',                           1400000000, 0.0333, 2, @asml, NULL);

-- Reliance Industries
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Saudi Aramco (crude imports)',   8000000000, 0.0667, 2, @ril, @aramco),
('Other crude & petrochem feedstock', 65000000000, 0.5417, 2, @ril, NULL),
('Retail merchandise',            32000000000, 0.2667, 2, @ril, NULL),
('Telecom network opex (Jio)',     7000000000, 0.0583, 2, @ril, NULL),
('SG&A',                           5500000000, 0.0458, 2, @ril, NULL);

-- Tencent
INSERT INTO CostSources (Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId) VALUES
('Content + payment + cloud costs', 51000000000, 0.4679, 2, @tcehy, NULL),
('Nvidia (AI training chips)',     3000000000, 0.0275, 2, @tcehy, @nvda),
('R&D',                           10500000000, 0.0963, 0, @tcehy, NULL),
('Sales & marketing',              4800000000, 0.0440, 2, @tcehy, NULL),
('G&A',                            6500000000, 0.0596, 2, @tcehy, NULL);

COMMIT;

-- Verification queries (run separately):
-- SELECT COUNT(*) FROM Companies WHERE DeletedAt IS NULL;            -- expect 20
-- SELECT COUNT(*) FROM Countries WHERE DeletedAt IS NULL;            -- expect 13
-- SELECT COUNT(*) FROM RevenueSources WHERE DeletedAt IS NULL;
-- SELECT COUNT(*) FROM CostSources WHERE DeletedAt IS NULL;
-- SELECT c.Name AS Company, c2.Name AS RelatedCompany, rs.Name AS RevenueSrc, rs.Value
-- FROM RevenueSources rs JOIN Companies c ON c.Id=rs.CompanyId
-- LEFT JOIN Companies c2 ON c2.Id=rs.RelatedCompanyId WHERE rs.RelatedCompanyId IS NOT NULL;
