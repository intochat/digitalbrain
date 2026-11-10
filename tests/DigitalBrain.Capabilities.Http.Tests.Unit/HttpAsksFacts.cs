namespace DigitalBrain.Capabilities.Http.Tests.Unit;

public sealed class HttpAsksFacts
{
    [Fact]
    public void GetJsonIsARepeatableAskKeyedByItsUrlUnlessAKeyIsGiven()
    {
        var x = new Asks<IHttpRead>("x");
        var byUrl = x.GetJson("https://example.org/a");
        var keyed = x.GetText("https://example.org/a", key: "feed");

        Assert.Equal(("x", "GetJson", "https://example.org/a", true), (byUrl.Slot, byUrl.Operation, byUrl.Key, byUrl.Repeatable));
        Assert.Equal("""{"url":"https://example.org/a"}""", byUrl.Arguments.GetRawText());
        Assert.Equal(("GetText", "feed", true), (keyed.Operation, keyed.Key, keyed.Repeatable));
    }
}
