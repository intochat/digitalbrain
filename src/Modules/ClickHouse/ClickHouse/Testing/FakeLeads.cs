using System.Text.Json;

namespace DigitalBrain.ClickHouse;

// A deterministic slice of the leads seed (Seeds/leads.sql) with the same tables and columns,
// so scenarios can assert on it without a server.
internal static class FakeLeads
{
    public static void Seed(FakeClickHouseProvider provider)
    {
        provider.RegisterTable("companies_current",
        [
            new("company_id", "String", "text"),
            new("name", "String", "text"),
            new("country", "LowCardinality(String)", "text"),
            new("city", "String", "text"),
            new("website", "String", "text"),
            new("industry", "LowCardinality(String)", "text"),
            new("activity_tags", "Array(String)", "text"),
            new("employee_count", "Nullable(UInt32)", "number"),
            new("revenue_eur", "Nullable(Float64)", "number"),
            new("founded_year", "Nullable(UInt16)", "number"),
            new("founded_on", "Nullable(Date)", "date"),
            new("is_active", "Bool", "boolean"),
            new("attributes", "JSON", "text"),
            new("version", "UInt64", "number"),
            new("updated_at", "DateTime", "text"),
        ],
        [
            Company("gb-001", "Thames Roofing Ltd", "GB", "London", "Construction", ["construction", "roofing"], 64, 8200000, 1998, "1998-04-12", true, """{"vat":"GB123456789","rating":4.6}"""),
            Company("gb-002", "Northgate Builders", "GB", "Manchester", "Construction", ["construction", "civil-engineering"], 120, 21500000, 1985, "1985-09-01", true, """{"vat":"GB223456789","rating":4.1}"""),
            Company("gb-003", "Pennine Slate & Tile", "GB", "Leeds", "Construction", ["roofing", "materials"], 18, 2400000, 2005, "2005-02-20", true, """{"vat":"GB323456789","rating":4.8}"""),
            Company("gb-005", "Orbit Software Ltd", "GB", "Cambridge", "Software", ["software", "saas"], 210, 34000000, 2010, "2010-11-03", true, """{"vat":"GB523456789","rating":4.4}"""),
            Company("gb-009", "Mersey Flat Roofing", "GB", "Liverpool", "Construction", ["roofing"], 9, 900000, 2019, "2019-01-10", true, """{"vat":"GB923456789","rating":4.7}"""),
            Company("gb-011", "Cotswold Stone Masons", "GB", "Cheltenham", "Construction", ["construction", "masonry"], null, null, 1972, null, false, """{"vat":"GB113456789"}"""),
            Company("de-001", "Rheinbau GmbH", "DE", "Köln", "Construction", ["construction", "civil-engineering"], 310, 58000000, 1979, "1979-03-14", true, """{"vat":"DE123456789","rating":4.2}"""),
            Company("de-002", "Dachwerk Berlin", "DE", "Berlin", "Construction", ["roofing", "construction"], 47, 6200000, 2002, "2002-10-22", true, """{"vat":"DE223456789","rating":4.6}"""),
            Company("de-008", "Isar Dachdecker", "DE", "München", "Construction", ["roofing"], 12, 1400000, 2014, "2014-06-18", true, """{"vat":"DE823456789","rating":4.7}"""),
            Company("cz-001", "Vltava Stavby s.r.o.", "CZ", "Praha", "Construction", ["construction", "civil-engineering"], 140, 19000000, 1994, "1994-07-11", true, """{"vat":"CZ12345678","rating":4.1}"""),
            Company("cz-002", "Střechy Morava", "CZ", "Brno", "Construction", ["roofing"], 22, 2100000, 2006, "2006-03-03", true, """{"vat":"CZ22345678","rating":4.5}"""),
            Company("cz-006", "Silesia Data", "CZ", "Ostrava", "Software", ["software", "analytics"], 19, 2300000, 2018, "2018-02-02", true, """{"vat":"CZ62345678","rating":4.3}"""),
        ], "ReplacingMergeTree");

        provider.RegisterTable("employees",
        [
            new("employee_id", "String", "text"),
            new("company_id", "String", "text"),
            new("full_name", "String", "text"),
            new("role", "String", "text"),
            new("seniority", "LowCardinality(String)", "text"),
            new("updated_at", "DateTime", "text"),
        ],
        [
            Row("emp-001", "gb-001", "Amelia Hart", "Managing Director", "executive", UpdatedAt),
            Row("emp-002", "gb-001", "Owen Price", "Site Manager", "senior", UpdatedAt),
            Row("emp-006", "de-001", "Lukas Brandt", "Geschäftsführer", "executive", UpdatedAt),
            Row("emp-010", "cz-001", "Petr Novák", "Jednatel", "executive", UpdatedAt),
        ]);

        provider.RegisterTable("facts",
        [
            new("fact_id", "String", "text"),
            new("company_id", "String", "text"),
            new("fact_type", "LowCardinality(String)", "text"),
            new("value", "String", "text"),
            new("source_id", "String", "text"),
            new("observed_at", "DateTime", "text"),
        ],
        [
            Row("fact-001", "gb-001", "certification", "NFRC member", "src-web-crawl", UpdatedAt),
            Row("fact-005", "de-001", "tender", "Rhine bridge maintenance framework", "src-web-crawl", UpdatedAt),
            Row("fact-008", "cz-001", "tender", "D1 motorway resurfacing", "src-ares", UpdatedAt),
        ]);

        provider.RegisterTable("sources",
        [
            new("source_id", "String", "text"),
            new("url", "String", "text"),
            new("kind", "LowCardinality(String)", "text"),
            new("fetched_at", "DateTime", "text"),
        ],
        [
            Row("src-web-crawl", "https://crawl.example/leads", "crawl", UpdatedAt),
            Row("src-ares", "https://ares.gov.cz/", "registry", UpdatedAt),
        ]);
    }

    private const string UpdatedAt = "2026-09-12T00:00:00";

    private static JsonElement[] Company(string id, string name, string country, string city, string industry, string[] tags,
        int? employees, double? revenue, int? foundedYear, string? foundedOn, bool active, string attributes)
        => Row(id, name, country, city, $"{id}.example", industry, JsonSerializer.Serialize(tags), employees, revenue, foundedYear, foundedOn, active, attributes, 1, UpdatedAt);

    private static JsonElement[] Row(params object?[] cells) => cells.Select(cell => JsonSerializer.SerializeToElement(cell)).ToArray();
}
