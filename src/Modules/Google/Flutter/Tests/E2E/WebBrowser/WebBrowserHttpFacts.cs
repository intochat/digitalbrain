using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.WebBrowser;

public sealed class WebBrowserHttpFacts
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task NavigateUpdatesSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        using var navigate = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/ui/browsers/docs/navigate",
            new { uri = "https://learn.microsoft.com/", title = "Docs" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, navigate.StatusCode);
        var state = await brain.HttpClient.GetFromJsonAsync<WebBrowserState>("/workspaces/workspace-a/ui/browsers/docs", Json, ct);
        Assert.Equal("https://learn.microsoft.com/", state!.Uri);
        Assert.Equal("Docs", state.Title);
    }
}