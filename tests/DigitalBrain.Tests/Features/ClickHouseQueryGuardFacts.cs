using DigitalBrain.ClickHouse;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClickHouseQueryGuardFacts
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("  select name from companies_current where country = 'GB' limit 10 ")]
    [InlineData("WITH totals AS (SELECT country, count() AS n FROM companies_current GROUP BY country) SELECT * FROM totals ORDER BY n DESC")]
    [InlineData("SELECT name FROM companies_current WHERE name = 'a--b'")]
    [InlineData("SELECT name FROM companies_current WHERE note = 'it''s; fine /* really */ # yes'")]
    [InlineData("SELECT `format`, \"settings\" FROM companies_current")]
    [InlineData("SELECT formatDateTime(now(), '%Y') AS year, created_at, deleted_at FROM facts")]
    [InlineData("SELECT * FROM companies_current WHERE hasAny(activity_tags, ['construction', 'roofing'])")]
    [InlineData("SELECT format('{}-{}', country, city) AS key FROM companies_current")]
    [InlineData("SELECT URLHash(website) AS h FROM companies_current")]
    public void Accepts_read_only_selects(string sql) => ClickHouseQueryGuard.Validate(sql);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INSERT INTO companies_current (company_id) VALUES ('x')")]
    [InlineData("SELECT 1; DROP TABLE companies_current")]
    [InlineData("SELECT 1 -- trailing comment")]
    [InlineData("SELECT /* block */ 1")]
    [InlineData("SELECT 1 # hash comment")]
    [InlineData("SELECT * FROM companies_current FORMAT JSON")]
    [InlineData("SELECT 1 SETTINGS max_threads = 1")]
    [InlineData("SELECT * FROM companies_current INTO OUTFILE 'leads.csv'")]
    [InlineData("DELETE FROM companies_current WHERE 1")]
    [InlineData("ALTER TABLE companies_current DELETE WHERE 1")]
    [InlineData("SELECT * FROM url('http://example.test/x.csv', CSV)")]
    [InlineData("SELECT * FROM remote('other-host', db.t)")]
    [InlineData("SELECT * FROM oss('http://internal/leads.csv', 'CSV', 'x String')")]
    [InlineData("SELECT * FROM icebergS3Cluster('c', 'http://internal/x')")]
    [InlineData("SELECT * FROM azureBlobStorageCluster('c', 'x', 'y')")]
    [InlineData("SELECT $a$'$a$ FROM url('http://169.254.169.254/latest/meta-data/', 'CSV', 'x String') WHERE $b$'$b$ != ''")]
    [InlineData("SELECT $$plain$$")]
    [InlineData("SELECT * FROM system.columns")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SHOW TABLES")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("SET max_threads = 1")]
    public void Rejects_everything_else(string sql)
    {
        var error = Assert.Throws<ArgumentException>(() => ClickHouseQueryGuard.Validate(sql));
        Assert.Contains("read-only SELECT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_oversized_statements()
    {
        var error = Assert.Throws<ArgumentException>(() => ClickHouseQueryGuard.Validate("SELECT '" + new string('x', 20_001) + "'"));
        Assert.Contains("20000 characters", error.Message, StringComparison.Ordinal);
    }
}
