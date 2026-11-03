namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

public sealed class ConnectionKeysFacts
{
    [Fact]
    public void ForThenParseRoundTrips()
    {
        var key = ConnectionKeys.For("b", "gmail", "1");
        Assert.Equal("b/connection/gmail/1", key);
        Assert.Equal(("b", "gmail", "1"), ConnectionKeys.Parse(key));
    }
}
