using DigitalBrain.Postgres;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresQueryGuardFacts
{
    [Theory]
    [InlineData("select ';' as value;", "select ';' as value")]
    [InlineData("select 'it''s; fine'; \r\n", "select 'it''s; fine'")]
    [InlineData("with items as (select 1 as id) select id from items;", "with items as (select 1 as id) select id from items")]
    [InlineData(" select 1 ", "select 1")]
    public void NormalizesOnlyTheFinalTerminator(string sql, string expected)
        => Assert.Equal(expected, PostgresQueryGuard.Normalize(sql));

    [Theory]
    [InlineData("")]
    [InlineData(";")]
    [InlineData("select 'unterminated;")]
    [InlineData("select 1; select 2;")]
    [InlineData("select 1;;")]
    [InlineData("select 1 /* comment */;")]
    [InlineData("delete from items;")]
    [InlineData("with changed as (delete from items returning *) select * from changed;")]
    [InlineData("select set_config('x', 'y', false);")]
    [InlineData("select \"set_config\"('x', 'y', false)")]
    [InlineData("select $$text$$")]
    public void RejectsUnsafeOrMalformedQueries(string sql)
        => Assert.Throws<ArgumentException>(() => PostgresQueryGuard.Normalize(sql));
}
