using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.UI;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class FabricSurfaceTests(AppHostFixture fixture)
{
    // C2 review gap 1: nothing pinned state recovery on the REAL Default blob provider —
    // EntityTests round-trip inside one activation in memory. Deactivate everything idle,
    // then prove the chart re-reads its points from grainstate blobs.
    [Fact]
    public async Task RendererWrittenChartStateSurvivesActivationCollection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var brain = fixture.BrainFor($"dur{Guid.NewGuid():N}"[..12]);
        var name = "chart-durability";

        await brain.FireAsync<IUIRenderer>(name, new ChartPoint("series", "before", 41), cancellationToken);
        var written = await brain.GetEntity<IChart>(name).Read();
        Assert.NotNull(written);
        Assert.Single(written!.Points);

        var management = (await fixture.GrainsAsync()).GetGrain<IManagementGrain>(0);
        await management.ForceActivationCollection(TimeSpan.Zero);

        var survived = await brain.GetEntity<IChart>(name).Read();
        Assert.NotNull(survived);
        var point = Assert.Single(survived!.Points);
        Assert.Equal(41, point.Value);
    }

    // C2 review gap 3: /brain/topology and /graph/events are shell-consumed, were rewritten
    // twice in C2, and had zero coverage. Smoke them over real HTTP with the real auth cookie.
    [Fact]
    public async Task BrainTopologyAndGraphEventsServeTheShellWire()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = fixture.CreateHttpClient("kernel");

        var login = await http.PostAsJsonAsync(
            "/auth/login", new { username = "owner", password = "ownerowner" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var topology = await http.GetAsync("/brain/topology", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, topology.StatusCode);
        var snapshot = await topology.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("neurons").ValueKind);
        Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("connections").ValueKind);
        Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("modules").ValueKind);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/graph/events?afterSequence=0");
        using var events = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        Assert.Equal("text/event-stream", events.Content.Headers.ContentType?.MediaType);
    }
}
