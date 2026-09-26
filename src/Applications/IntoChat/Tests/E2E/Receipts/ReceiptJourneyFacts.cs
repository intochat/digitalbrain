using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Metering;
using IntoChat.Tests.E2E.Agent;
using IntoChat.Tests.E2E.Workspace;

namespace IntoChat.Tests.E2E.Receipts;

public sealed class ReceiptJourneyFacts
{
    [Fact(Timeout = 240_000)]
    public async Task EveryIntentEmitsAShadowPricedReceiptFromDurableUsage()
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
        var receipt = ReceiptFrom(stream);
        Assert.Equal("Succeeded", receipt.GetProperty("outcome").GetString());
        Assert.True(receipt.GetProperty("shadow").GetBoolean());
        Assert.Contains(receipt.GetProperty("calls").EnumerateArray(), call =>
            call.GetProperty("appId").GetString() == "show_supabase_query_table" && call.GetProperty("succeeded").GetBoolean());
        Assert.Contains(receipt.GetProperty("touched").EnumerateArray(), entry =>
            entry.GetProperty("source").GetString() == "Active leads" && entry.GetProperty("readOnly").GetBoolean());

        // The card is priced from the intent's durable usage batch.
        var usage = await brain.Get<IIntentUsage>("receipt-run").ReadAsync(ct);
        Assert.Equal(usage.Entries.Length, receipt.GetProperty("modelCalls").GetInt32());
        Assert.NotEmpty(usage.Entries);

        // A second intent receives its own streamed recap and usage batch.
        using var replay = await brain.HttpClient.PostAsJsonAsync("/agent",
            new { workspaceId = "receipts", threadId = "thread", runId = "receipt-run-2", messages = new[] { new { role = "user", content = "Show leads" } } }, ct);
        var secondStream = await replay.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_FINISHED", secondStream);
        Assert.Equal("RECEIPT", ReceiptFrom(secondStream).GetProperty("type").GetString());
        Assert.NotEmpty((await brain.Get<IIntentUsage>("receipt-run-2").ReadAsync(ct)).Entries);
    }

    private static JsonElement ReceiptFrom(string stream)
    {
        foreach (var line in stream.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) { continue; }
            using var document = JsonDocument.Parse(line[6..]);
            if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "RECEIPT")
            {
                return document.RootElement.Clone();
            }
        }

        throw new Xunit.Sdk.XunitException("The agent stream did not contain a receipt card.");
    }
}
