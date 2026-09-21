using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chart;
using DigitalBrain.Flutter.Chart.Signals;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Chart;

public sealed class ChartHttpFacts
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task RenderMatchesRead()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        var chart = brain.Get<IChart>("btc");
        await using var frames = await brain.Observe<ChartChanged>(chart, ct);
        using var render = await brain.HttpClient.PostAsJsonAsync("/ui/charts/btc/render",
            new { title = "BTC", kind = "line", points = new[] { new { eventId = "t0", label = "open", value = 64000 } } }, ct);
        Assert.Equal(HttpStatusCode.Accepted, render.StatusCode);
        Assert.Equal("BTC", (await frames.NextAsync(ct: ct)).Title);
        var http = await brain.HttpClient.GetFromJsonAsync<ChartState>("/ui/charts/btc", Json, ct);
        Assert.Equal("line", http!.Kind);
        Assert.Equal(64000, http.Points[0].Value);
        Assert.Equal("BTC", (await chart.Read()).Title);
    }
}
