namespace IntoChat.Tests;

public sealed class HealthFacts
{
    [Fact]
    public async Task IntoChatHealthReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = await E2EDigitalBrain.StartAsync<Projects.IntoChat_AppHost>(
            new E2EOptions
            {
                Args = ["DigitalBrain:Flutter:Hosting:Kind=None"],
                WaitFor = ["IntoChat"],
            },
            ct);
        using var http = app.CreateHttpClient("IntoChat");
        using var response = await http.GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
