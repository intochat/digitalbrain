using DigitalBrain.Flutter;
namespace IntoChat.Tests.E2E.Composition;

[Collection(IntoChatHostCollection.Name)]
public sealed class ApplicationStartupFacts(IntoChatHostFixture host)
{
    [Fact]
    public async Task IntoChatHealthReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        using var response = await brain.HttpClient.GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
