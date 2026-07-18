using Ino.Core;
using Xunit;

namespace Ino.Core.Tests;

public class NeuronIdTests
{
    [Fact]
    public void From_produces_record_with_value()
    {
        var id = NeuronId.From("travel.plan-trip");

        Assert.Equal("travel.plan-trip", id.Value);
        Assert.Equal("travel.plan-trip", id.ToString());
    }

    [Fact]
    public void Two_values_with_same_string_are_equal()
    {
        Assert.Equal(
            NeuronId.From("travel.plan-trip"),
            NeuronId.From("travel.plan-trip"));
    }

    [Fact]
    public void Two_values_with_different_strings_are_not_equal()
    {
        Assert.NotEqual(
            NeuronId.From("travel.find-flights"),
            NeuronId.From("travel.plan-trip"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_rejects_empty_or_whitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => NeuronId.From(value));
    }
}
