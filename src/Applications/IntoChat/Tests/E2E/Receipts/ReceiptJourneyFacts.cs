using System.Net.Http.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Metering;
using DigitalBrain.Receipts;
using IntoChat.Tests.E2E.Agent;
using IntoChat.Tests.E2E.Workspace;

namespace IntoChat.Tests.E2E.Receipts;

public sealed class ReceiptJourneyFacts
{
    [Fact(Timeout = 240_000)]
    public async Task EveryIntentGetsADurableShadowPricedReceipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .StartAsync(ct);
        await LeadData.SeedAsync(brain, "Receipt run", ct);

        using var response = await brain.HttpClient.PostAsJsonAsync("/agent",
            new { workspaceId = "receipts", threadId = "thread", runId = "receipt-run", messages = new[] { new { role = "user", content = "Show leads" } } }, ct);
        var stream = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_FINISHED", stream);
        Assert.Contains("RECEIPT", stream);

        var receipt = await brain.Get<IReceipt>("receipt-run").Read();
        Assert.NotNull(receipt);
        var content = receipt!.Content;
        Assert.Equal(ReceiptOutcome.Succeeded, content.Outcome);
        Assert.True(content.ShadowPriced);
        Assert.Equal(0m, content.ApprovedComputeLimit);
        Assert.Equal(content.EstimatedCompute, content.ActualCompute);
        Assert.Null(content.FailureExplanation);
        Assert.True(content.FirstTry);
        Assert.Contains(content.Calls, call => call.AppId == "show_supabase_query_table" && call.Succeeded);
        Assert.Contains(content.Touched, entry => entry.Source == "Active leads" && entry.ReadOnly);

        // The shadow price is derived from the durable usage batch, never from traces, and the
        // receipt survives re-reading the grain (it is not an artifact of the request's traces).
        var usage = await brain.Get<IIntentUsage>("receipt-run").ReadAsync(ct);
        Assert.Equal(usage.Entries.Length, content.ModelCalls);
        Assert.True(content.ModelCalls >= 1);
        Assert.Equal(receipt.WrittenAt, (await brain.Get<IReceipt>("receipt-run").Read())!.WrittenAt);

        // A second intent gets its own durable row, and the first is untouched.
        using var replay = await brain.HttpClient.PostAsJsonAsync("/agent",
            new { workspaceId = "receipts", threadId = "thread", runId = "receipt-run-2", messages = new[] { new { role = "user", content = "Show leads" } } }, ct);
        Assert.Contains("RUN_FINISHED", await replay.Content.ReadAsStringAsync(ct));
        var second = await brain.Get<IReceipt>("receipt-run-2").Read();
        Assert.NotNull(second);
        Assert.Equal("receipt-run-2", second!.IntentId);
        Assert.Equal("receipt-run", (await brain.Get<IReceipt>("receipt-run").Read())!.IntentId);
    }
}
