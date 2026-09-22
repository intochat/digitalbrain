using DigitalBrain.Supabase;
using Xunit;

namespace DigitalBrain.Modules.Supabase.Tests.Unit.Query;

public sealed class SupabaseQueryGuardFacts
{
    [Theory]
    [InlineData("select ';' as value;", "select ';' as value")]
    [InlineData("select 'it''s; fine' as value; \r\n", "select 'it''s; fine' as value")]
    [InlineData("with customers as (select 1 as id) select id from customers;", "with customers as (select 1 as id) select id from customers")]
    [InlineData(" select 1 ", "select 1")]
    public void NormalizesOnlyTheFinalTerminator(string input, string expected)
        => Assert.Equal(expected, SupabaseQueryGuard.Normalize(input));

    [Theory]
    [InlineData(";")]
    [InlineData("select 'unterminated;")]
    [InlineData("select 1; select 2;")]
    [InlineData("select 1;;")]
    [InlineData("select 1 /* comment */;")]
    [InlineData("delete from customers;")]
    [InlineData("with changed as (delete from customers returning *) select * from changed;")]
    [InlineData("select set_config('x', 'y', false);")]
    public void RejectsUnsafeOrMalformedQueries(string input)
        => Assert.Throws<ArgumentException>(() => SupabaseQueryGuard.Normalize(input));
}