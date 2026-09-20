using DigitalBrain.Flutter;
namespace IntoChat.Tests;

public sealed class HealthFacts
{
    [Fact]
    public async Task IntoChatHealthReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.StartAsync<Projects.IntoChat_AppHost>(DigitalBrainOptions.Headless, ct);
        using var response = await brain.HttpClient.GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
