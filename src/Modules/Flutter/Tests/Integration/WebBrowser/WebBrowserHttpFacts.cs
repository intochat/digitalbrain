using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Integration.WebBrowser;

public sealed class WebBrowserHttpFacts
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task NavigateUpdatesSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        using var navigate = await brain.HttpClient.PostAsJsonAsync("/ui/browsers/docs/navigate",
            new { uri = "https://learn.microsoft.com/", title = "Docs" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, navigate.StatusCode);
        var state = await brain.HttpClient.GetFromJsonAsync<WebBrowserState>("/ui/browsers/docs", Json, ct);
        Assert.Equal("https://learn.microsoft.com/", state!.Uri);
        Assert.Equal("Docs", state.Title);
    }
}
