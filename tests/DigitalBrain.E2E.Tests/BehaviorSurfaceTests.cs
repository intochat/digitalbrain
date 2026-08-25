using System.Net;
using System.Net.Http.Json;
using DigitalBrain.SmartPrompt;
using DigitalBrain.UI;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class BehaviorSurfaceTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Eight_seeded_behaviors_are_green_and_fake_runnable()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var behaviors = await http.GetFromJsonAsync<List<BehaviorSummary>>(
            "/behaviors", TestContext.Current.CancellationToken);
        Assert.NotNull(behaviors);
        Assert.Equal(8, behaviors.Count);
        Assert.All(behaviors, item =>
        {
            Assert.True(item.Active, item.Name);
            Assert.True(item.LastTest?.AllGreen, item.Name);
        });

        foreach (var behavior in behaviors)
        {
            var response = await http.PostAsync(
                $"/behaviors/{behavior.Name}/fake", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Local_Gemma_generates_only_compilable_Reqnroll_source()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.PostAsJsonAsync("/behaviors/generate",
            new { request = "Create a behavior that notifies me when Bitcoin is above 90000." },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var generated = await response.Content.ReadFromJsonAsync<BehaviorGeneration>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(generated);
        Assert.Equal("gemma4:e2b", generated.Model);
        Assert.True(generated.Compilation.Success,
            string.Join(Environment.NewLine, generated.Compilation.Diagnostics.Select(x => x.Message)));
    }

    [Fact]
    public async Task One_X_ingress_post_is_delivered_to_the_shared_Bitcoin_behavior()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var id = $"e2e-x-{Guid.NewGuid():N}";
        var response = await http.PostAsJsonAsync("/ingress/x/posts", new
        {
            id,
            account = "@elonmusk",
            text = "Bitcoin reaches 123456",
            value = 123456,
            sourceUri = $"https://x.com/elonmusk/status/{id}",
            occurredAt = "2026-08-25T12:00:00Z",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        ChartState? chart = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            var read = await http.GetAsync("/behavior-charts/bitcoin_tracker", timeout.Token);
            if (read.IsSuccessStatusCode)
            {
                chart = await read.Content.ReadFromJsonAsync<ChartState>(cancellationToken: timeout.Token);
                if (chart?.Points.Any(point => point.EventId == id && point.SourceUri?.Contains(id) == true) == true)
                {
                    break;
                }
            }
            await Task.Delay(100, timeout.Token);
        }
        Assert.NotNull(chart);
        Assert.Contains(chart!.Points, point => point.EventId == id && point.Value == 123456);
    }

    [Fact]
    public async Task Assistant_can_generate_test_and_activate_a_behavior_on_demand()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var send = await http.PostAsJsonAsync(
            HttpSurfacePaths.OwnerCommandsPath,
            new
            {
                kind = "chat.send",
                chatName = $"behavior-tool-{Guid.NewGuid():N}",
                text = "create a behavior that notifies me when Bitcoin is high",
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var read = await http.GetAsync(
            "/behaviors/generated-bitcoin-alert",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var behavior = await read.Content.ReadFromJsonAsync<BehaviorSummary>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(behavior?.Active);
        Assert.True(behavior?.LastTest?.AllGreen);
    }
}
