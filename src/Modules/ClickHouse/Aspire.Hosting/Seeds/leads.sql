-- Dev seed for the ClickHouse module: a small lead-generation dataset.
-- Idempotent on purpose: persistent containers re-run this file only on an empty data dir,
-- and CLICKHOUSE_ALWAYS_RUN_INITDB_SCRIPTS=1 re-runs it on every start.
SET allow_experimental_json_type = 1;

CREATE DATABASE IF NOT EXISTS digitalbrain;

CREATE TABLE IF NOT EXISTS digitalbrain.sources
(
    source_id String,
    url String,
    kind LowCardinality(String),
    fetched_at DateTime
)
ENGINE = MergeTree
ORDER BY (kind, source_id);

CREATE TABLE IF NOT EXISTS digitalbrain.companies_current
(
    company_id String,
    name String,
    country LowCardinality(String),
    city String,
    website String,
    industry LowCardinality(String),
    activity_tags Array(String),
    employee_count Nullable(UInt32),
    revenue_eur Nullable(Float64),
    founded_year Nullable(UInt16),
    founded_on Nullable(Date),
    is_active Bool,
    attributes JSON,
    version UInt64,
    updated_at DateTime
)
ENGINE = ReplacingMergeTree(version)
ORDER BY (country, company_id);

CREATE TABLE IF NOT EXISTS digitalbrain.facts
(
    fact_id String,
    company_id String,
    fact_type LowCardinality(String),
    value String,
    source_id String,
    observed_at DateTime
)
ENGINE = MergeTree
ORDER BY (company_id, fact_type, observed_at);

CREATE TABLE IF NOT EXISTS digitalbrain.employees
(
    employee_id String,
    company_id String,
    full_name String,
    role String,
    seniority LowCardinality(String),
    updated_at DateTime
)
ENGINE = MergeTree
ORDER BY (company_id, employee_id);

INSERT INTO digitalbrain.sources
SELECT source_id, url, kind, now()
FROM values(
    'source_id String, url String, kind String',
    ('src-companies-house', 'https://find-and-update.company-information.service.gov.uk/', 'registry'),
    ('src-handelsregister', 'https://www.handelsregister.de/', 'registry'),
    ('src-ares', 'https://ares.gov.cz/', 'registry'),
    ('src-web-crawl', 'https://crawl.example/leads', 'crawl'),
    ('src-linkedin', 'https://www.linkedin.com/', 'social'))
WHERE (SELECT count() FROM digitalbrain.sources) = 0;

INSERT INTO digitalbrain.companies_current
SELECT
    company_id,
    name,
    country,
    city,
    website,
    industry,
    activity_tags,
    employee_count,
    revenue_eur,
    founded_year,
    CAST(founded_on, 'Nullable(Date)'),
    is_active,
    CAST(attributes, 'JSON'),
    1,
    now()
FROM values(
    'company_id String, name String, country String, city String, website String, industry String, activity_tags Array(String), employee_count Nullable(UInt32), revenue_eur Nullable(Float64), founded_year Nullable(UInt16), founded_on Nullable(String), is_active Bool, attributes String',
    ('gb-001', 'Thames Roofing Ltd', 'GB', 'London', 'thamesroofing.example', 'Construction', ['construction', 'roofing'], 64, 8200000, 1998, '1998-04-12', true, '{"vat":"GB123456789","rating":4.6}'),
    ('gb-002', 'Northgate Builders', 'GB', 'Manchester', 'northgatebuilders.example', 'Construction', ['construction', 'civil-engineering'], 120, 21500000, 1985, '1985-09-01', true, '{"vat":"GB223456789","rating":4.1}'),
    ('gb-003', 'Pennine Slate & Tile', 'GB', 'Leeds', 'pennineslate.example', 'Construction', ['roofing', 'materials'], 18, 2400000, 2005, '2005-02-20', true, '{"vat":"GB323456789","rating":4.8}'),
    ('gb-004', 'Bristol Scaffold Co', 'GB', 'Bristol', 'bristolscaffold.example', 'Construction', ['construction', 'scaffolding'], 42, 5100000, 2001, '2001-06-15', true, '{"vat":"GB423456789","rating":3.9}'),
    ('gb-005', 'Orbit Software Ltd', 'GB', 'Cambridge', 'orbitsoftware.example', 'Software', ['software', 'saas'], 210, 34000000, 2010, '2010-11-03', true, '{"vat":"GB523456789","rating":4.4}'),
    ('gb-006', 'Kestrel Analytics', 'GB', 'Edinburgh', 'kestrelanalytics.example', 'Software', ['software', 'analytics'], 35, 4700000, 2016, '2016-03-28', true, '{"vat":"GB623456789","rating":4.2}'),
    ('gb-007', 'Highland Timber Frames', 'GB', 'Inverness', 'highlandtimber.example', 'Construction', ['construction', 'timber'], 27, 3300000, 2008, '2008-08-08', true, '{"vat":"GB723456789","rating":4.5}'),
    ('gb-008', 'Severn Groundworks', 'GB', 'Gloucester', 'severngroundworks.example', 'Construction', ['construction', 'groundworks'], 88, 12800000, 1993, '1993-05-19', true, '{"vat":"GB823456789","rating":4.0}'),
    ('gb-009', 'Mersey Flat Roofing', 'GB', 'Liverpool', 'merseyflatroofing.example', 'Construction', ['roofing'], 9, 900000, 2019, '2019-01-10', true, '{"vat":"GB923456789","rating":4.7}'),
    ('gb-010', 'Albion Cloud Systems', 'GB', 'London', 'albioncloud.example', 'Software', ['software', 'cloud', 'devops'], 540, 96000000, 2007, '2007-07-07', true, '{"vat":"GB103456789","rating":4.3}'),
    ('gb-011', 'Cotswold Stone Masons', 'GB', 'Cheltenham', 'cotswoldmasons.example', 'Construction', ['construction', 'masonry'], NULL, NULL, 1972, NULL, false, '{"vat":"GB113456789"}'),
    ('de-001', 'Rheinbau GmbH', 'DE', 'Köln', 'rheinbau.example', 'Construction', ['construction', 'civil-engineering'], 310, 58000000, 1979, '1979-03-14', true, '{"vat":"DE123456789","rating":4.2}'),
    ('de-002', 'Dachwerk Berlin', 'DE', 'Berlin', 'dachwerk-berlin.example', 'Construction', ['roofing', 'construction'], 47, 6200000, 2002, '2002-10-22', true, '{"vat":"DE223456789","rating":4.6}'),
    ('de-003', 'Neckar Softwarehaus', 'DE', 'Stuttgart', 'neckar-software.example', 'Software', ['software', 'erp'], 150, 27000000, 1996, '1996-04-01', true, '{"vat":"DE323456789","rating":4.0}'),
    ('de-004', 'Hanse Fassaden AG', 'DE', 'Hamburg', 'hanse-fassaden.example', 'Construction', ['construction', 'facades'], 205, 39000000, 1988, '1988-12-05', true, '{"vat":"DE423456789","rating":4.1}'),
    ('de-005', 'Alpen Holzbau', 'DE', 'München', 'alpen-holzbau.example', 'Construction', ['construction', 'timber'], 33, 4100000, 2011, '2011-05-30', true, '{"vat":"DE523456789","rating":4.9}'),
    ('de-006', 'Spree Data Labs', 'DE', 'Berlin', 'spreedata.example', 'Software', ['software', 'analytics', 'ai'], 72, 11000000, 2015, '2015-09-09', true, '{"vat":"DE623456789","rating":4.4}'),
    ('de-007', 'Ruhr Tiefbau', 'DE', 'Essen', 'ruhr-tiefbau.example', 'Construction', ['construction', 'groundworks'], 96, 15500000, 1990, '1990-02-27', true, '{"vat":"DE723456789","rating":3.8}'),
    ('de-008', 'Isar Dachdecker', 'DE', 'München', 'isar-dach.example', 'Construction', ['roofing'], 12, 1400000, 2014, '2014-06-18', true, '{"vat":"DE823456789","rating":4.7}'),
    ('de-009', 'Elbe Digital', 'DE', 'Dresden', 'elbe-digital.example', 'Software', ['software', 'saas'], 58, 8900000, 2012, '2012-01-16', true, '{"vat":"DE923456789","rating":4.3}'),
    ('de-010', 'Main Gerüstbau', 'DE', 'Frankfurt', 'main-geruest.example', 'Construction', ['construction', 'scaffolding'], 61, 7300000, 1999, '1999-08-23', true, '{"vat":"DE103456789","rating":4.0}'),
    ('cz-001', 'Vltava Stavby s.r.o.', 'CZ', 'Praha', 'vltavastavby.example', 'Construction', ['construction', 'civil-engineering'], 140, 19000000, 1994, '1994-07-11', true, '{"vat":"CZ12345678","rating":4.1}'),
    ('cz-002', 'Střechy Morava', 'CZ', 'Brno', 'strechymorava.example', 'Construction', ['roofing'], 22, 2100000, 2006, '2006-03-03', true, '{"vat":"CZ22345678","rating":4.5}'),
    ('cz-003', 'Bohemia Software', 'CZ', 'Praha', 'bohemiasoftware.example', 'Software', ['software', 'saas'], 95, 12500000, 2009, '2009-10-10', true, '{"vat":"CZ32345678","rating":4.2}'),
    ('cz-004', 'Labe Fasády', 'CZ', 'Hradec Králové', 'labefasady.example', 'Construction', ['construction', 'facades'], 38, 4400000, 2003, '2003-04-25', true, '{"vat":"CZ42345678","rating":4.0}'),
    ('cz-005', 'Moravia Dřevostavby', 'CZ', 'Olomouc', 'moraviadrevo.example', 'Construction', ['construction', 'timber'], 51, 6600000, 2000, '2000-09-14', true, '{"vat":"CZ52345678","rating":4.6}'),
    ('cz-006', 'Silesia Data', 'CZ', 'Ostrava', 'silesiadata.example', 'Software', ['software', 'analytics'], 19, 2300000, 2018, '2018-02-02', true, '{"vat":"CZ62345678","rating":4.3}'),
    ('cz-007', 'Krkonoše Střechy', 'CZ', 'Liberec', 'krkonosestrechy.example', 'Construction', ['roofing', 'construction'], 7, 600000, 2020, '2020-05-05', true, '{"vat":"CZ72345678","rating":4.8}'),
    ('cz-008', 'Sázava Zemní práce', 'CZ', 'Benešov', 'sazavazemni.example', 'Construction', ['construction', 'groundworks'], 44, 5200000, 1997, '1997-11-20', true, '{"vat":"CZ82345678","rating":3.9}'),
    ('cz-009', 'Praha Cloud', 'CZ', 'Praha', 'prahacloud.example', 'Software', ['software', 'cloud'], 130, 21000000, 2013, '2013-06-06', true, '{"vat":"CZ92345678","rating":4.4}'))
WHERE (SELECT count() FROM digitalbrain.companies_current) = 0;

INSERT INTO digitalbrain.employees
SELECT employee_id, company_id, full_name, role, seniority, now()
FROM values(
    'employee_id String, company_id String, full_name String, role String, seniority String',
    ('emp-001', 'gb-001', 'Amelia Hart', 'Managing Director', 'executive'),
    ('emp-002', 'gb-001', 'Owen Price', 'Site Manager', 'senior'),
    ('emp-003', 'gb-002', 'Priya Nair', 'Commercial Director', 'executive'),
    ('emp-004', 'gb-005', 'Tom Whitfield', 'CTO', 'executive'),
    ('emp-005', 'gb-010', 'Sarah Okafor', 'VP Engineering', 'executive'),
    ('emp-006', 'de-001', 'Lukas Brandt', 'Geschäftsführer', 'executive'),
    ('emp-007', 'de-002', 'Mia Schuster', 'Dachdeckermeisterin', 'senior'),
    ('emp-008', 'de-003', 'Jonas Keller', 'Head of Product', 'senior'),
    ('emp-009', 'de-006', 'Lena Vogt', 'Data Scientist', 'mid'),
    ('emp-010', 'cz-001', 'Petr Novák', 'Jednatel', 'executive'),
    ('emp-011', 'cz-003', 'Jana Dvořáková', 'Engineering Manager', 'senior'),
    ('emp-012', 'cz-009', 'Martin Svoboda', 'Cloud Architect', 'senior'))
WHERE (SELECT count() FROM digitalbrain.employees) = 0;

INSERT INTO digitalbrain.facts
SELECT fact_id, company_id, fact_type, value, source_id, now()
FROM values(
    'fact_id String, company_id String, fact_type String, value String, source_id String',
    ('fact-001', 'gb-001', 'certification', 'NFRC member', 'src-web-crawl'),
    ('fact-002', 'gb-002', 'tender', 'Awarded A1 bypass phase 2', 'src-web-crawl'),
    ('fact-003', 'gb-005', 'funding', 'Series B, 18M GBP', 'src-web-crawl'),
    ('fact-004', 'gb-010', 'hiring', '12 open engineering roles', 'src-linkedin'),
    ('fact-005', 'de-001', 'tender', 'Rhine bridge maintenance framework', 'src-web-crawl'),
    ('fact-006', 'de-002', 'certification', 'Innungsbetrieb', 'src-handelsregister'),
    ('fact-007', 'de-006', 'funding', 'Seed, 3M EUR', 'src-web-crawl'),
    ('fact-008', 'cz-001', 'tender', 'D1 motorway resurfacing', 'src-ares'),
    ('fact-009', 'cz-003', 'hiring', '4 open backend roles', 'src-linkedin'),
    ('fact-010', 'cz-009', 'partnership', 'Azure partner', 'src-web-crawl'))
WHERE (SELECT count() FROM digitalbrain.facts) = 0;
