using FluentAssertions;
using Ino.NeuronTesting.Bdd;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public sealed class KeyValueParserTests
{
    [Theory]
    [InlineData("flightId=\"FL-001\"", "flightId", "FL-001")]
    [InlineData("flightId=FL-001", "flightId", "FL-001")]
    [InlineData("rainProbability=0.85", "rainProbability", "0.85")]
    public void Parses_single_kv(string input, string expectedKey, string expectedValue)
    {
        var dict = KeyValueParser.Parse(input);
        dict.Should().ContainKey(expectedKey).WhoseValue.Should().Be(expectedValue);
    }

    [Fact]
    public void Parses_multiple_kv_separated_by_commas()
    {
        var dict = KeyValueParser.Parse("flightId=\"FL-001\", price=180, airline=\"ANA\"");
        dict.Should().HaveCount(3);
        dict["flightId"].Should().Be("FL-001");
        dict["price"].Should().Be("180");
        dict["airline"].Should().Be("ANA");
    }
}
