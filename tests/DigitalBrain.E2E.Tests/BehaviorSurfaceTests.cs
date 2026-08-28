using System.Net;
using System.Net.Http.Json;
using DigitalBrain.SmartPrompt;
using DigitalBrain.UI;
using DigitalBrain.Kernel;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Integrations.Mcp;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class BehaviorSurfaceTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Nine_seeded_behaviors_are_green_and_fake_runnable()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var behaviors = await http.GetFromJsonAsync<List<BehaviorSummary>>(
            "/behaviors", TestContext.Current.CancellationToken);
        Assert.NotNull(behaviors);
        var seeded = behaviors.Where(item => BehaviorExamples.Find(item.Name) is not null).ToArray();
        Assert.Equal(9, seeded.Length);
        Assert.All(seeded, item =>
        {
            Assert.True(item.Active, item.Name);
            Assert.True(item.LastTest?.AllGreen, item.Name);
        });

        foreach (var behavior in seeded)
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

        var catalog = await http.GetFromJsonAsync<List<BehaviorSummary>>(
            "/behaviors", TestContext.Current.CancellationToken);
        Assert.Contains(catalog!, item => item.Name == "generated-bitcoin-alert");
    }

    [Fact]
    public async Task Assistant_chat_learns_then_runs_the_salesforce_enrichment_experience_through_fake_mcps()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var correction = await http.PostAsJsonAsync(
            HttpSurfacePaths.OwnerCommandsPath,
            new
            {
                kind = "chat.send",
                chatName = $"learning-tool-{Guid.NewGuid():N}",
                text = "Do this differently: preserve verified Salesforce fields when enriching accounts.",
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);

        var learned = false;
        using (var learningTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            while (!learningTimeout.IsCancellationRequested)
            {
                var experience = await http.GetFromJsonAsync<BehaviorSummary>(
                    "/behaviors/salesforce-account-enrichment", learningTimeout.Token);
                if (experience is { Active: true }
                    && experience.Source.Contains("preserve verified Salesforce fields", StringComparison.Ordinal))
                {
                    learned = true;
                    break;
                }
                await Task.Delay(100, learningTimeout.Token);
            }
        }
        Assert.True(learned, "Assistant did not learn and activate the explicit correction.");

        var main = fixture.BrainFor(DigitalBrainNames.DefaultOwner).GetGrainProxy<IChat>("main");
        var before = (await main.Read()).Turns.Count;
        var send = await http.PostAsJsonAsync(
            HttpSurfacePaths.OwnerCommandsPath,
            new
            {
                kind = "chat.send",
                chatName = $"enrichment-tool-{Guid.NewGuid():N}",
                text = "Enrich the company account for the new email from vlad@intochat.io in Salesforce.",
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var completed = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            if ((await main.Read()).Turns.Skip(before)
                .Any(turn => turn.Text.Contains("001INTOCHAT", StringComparison.Ordinal)))
            {
                completed = true;
                break;
            }
            await Task.Delay(100, timeout.Token);
        }
        Assert.True(completed, "Chat did not complete the MCP-backed Salesforce enrichment experience.");

        using var salesforceHttp = fixture.CreateHttpClient("fake-salesforce-mcp", "http");
        var mcp = new McpIntegrationClient();
        var account = await mcp.CallAsync(
            new McpIntegrationEndpoint(
                "fake-salesforce-mcp",
                new Uri(salesforceHttp.BaseAddress!, "/mcp")),
            "soqlQuery",
            new Dictionary<string, object?>
            {
                ["query"] = "SELECT Id, Description, DescriptionVerified FROM Account WHERE Id = '001INTOCHAT' LIMIT 1",
            },
            TestContext.Current.CancellationToken);
        var record = Assert.Single(account.GetProperty("records").EnumerateArray());
        Assert.True(record.GetProperty("DescriptionVerified").GetBoolean());
        Assert.Equal("Verified customer conversation platform.", record.GetProperty("Description").GetString());
    }
}
