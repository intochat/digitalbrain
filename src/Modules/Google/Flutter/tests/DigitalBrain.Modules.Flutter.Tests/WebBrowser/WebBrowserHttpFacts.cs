using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.WebBrowser;

[Collection(FlutterHostCollection.Name)]
public sealed class WebBrowserHttpFacts(FlutterHostFixture host)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task NavigateUpdatesSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = host.Brain;
        var ws = host.Workspace();
        using var navigate = await brain.HttpClient.PostAsJsonAsync($"/brains/{ws}/ui/browsers/docs/navigate",
            new { uri = "https://learn.microsoft.com/", title = "Docs" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, navigate.StatusCode);
        var state = await brain.HttpClient.GetFromJsonAsync<WebBrowserState>($"/brains/{ws}/ui/browsers/docs", Json, ct);
        Assert.Equal("https://learn.microsoft.com/", state!.Uri);
        Assert.Equal("Docs", state.Title);
    }
}
