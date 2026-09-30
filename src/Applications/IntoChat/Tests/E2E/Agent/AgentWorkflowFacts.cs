using DigitalBrain.Core.Enforcement;
using DigitalBrain.Assistant;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Metering;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using IntoChat.Tests.E2E.Workspace;
using Npgsql;
using DigitalBrain.Identity;

namespace IntoChat.Tests.E2E.Agent;

public sealed class AgentWorkflowFacts
{
    [Fact(Timeout = 240_000)]
    public async Task EmptyQueryIsRealAndFailedToolsNeverReportSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .StartAsync(ct);
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        async Task<string> Ask(string run)
        {
            using var response = await brain.HttpClient.PostAsJsonAsync("/agent", new { brainId = "failures", threadId = "thread", runId = run, messages = new[] { new { role = "user", content = "Show leads" } } }, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        model.Sql = "select id, company, email from leads where false";
        var emptyStream = await Ask("empty");
        Assert.True(emptyStream.Contains("RUN_FINISHED", StringComparison.Ordinal), emptyStream + "\n" + string.Join("\n", model.Errors) + "\n" + string.Join("\n", model.Requests.Select(r => r.GetRawText())));
        var workspace = brain.Get<IWorkspace>(BrainScope.Create("owner", "failures").Id);
        var window = Assert.Single((await workspace.Read()).Windows);
        Assert.Empty((await brain.Get<DigitalBrain.Supabase.Tables.ISupabaseTable>(window.Reference.NeuronId).Read(new(0, 25)))!.Rows);
        model.Sql = "delete from leads";
        var invalid = await Ask("invalid");
        Assert.Contains("RUN_ERROR", invalid);
        Assert.DoesNotContain("RUN_FINISHED", invalid);
        // The endpoint still flushes the failed intent's usage in one durable batch.
        Assert.NotEmpty((await brain.Get<IIntentUsage>(ComputeUsageEndpoints.IntentId(BrainScope.Create("owner", "failures").Id, "thread", "invalid")).ReadAsync(ct)).Entries);
        // P1.2: a failed run keeps its turn in the conversation history.
        var failedAgent = brain.Get<IAgent>(AssistantConversations.Key(BrainScope.Create("owner", "failures").Id, "thread"));
        Assert.Contains((await failedAgent.ReadConversation(ct)).Turns, turn => turn.RunId == "invalid");
        model.Sql = "select company from leads";
        model.BeforeTable = token => LeadData.DropAsync(brain, token);
        var unavailable = await Ask("unavailable");
        Assert.Contains("RUN_ERROR", unavailable);
        Assert.DoesNotContain("RUN_FINISHED", unavailable);
        Assert.Contains((await failedAgent.ReadConversation(ct)).Turns, turn => turn.RunId == "unavailable");
        Assert.Single((await workspace.Read()).Windows);
    }

    [Fact(Timeout = 240_000)]
    public async Task DisconnectInterruptsRunAndRejectsConcurrentSubmission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .StartAsync(ct);
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        model.Delay = TimeSpan.FromMinutes(1);
        var input = new { brainId = "cancel", threadId = "thread", runId = "cancel-run", messages = new[] { new { role = "user", content = "Show leads" } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/agent") { Content = JsonContent.Create(input) };
        using var response = await brain.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await model.ToolRequested.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        using var duplicate = await brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        var duplicateStream = await duplicate.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_ERROR", duplicateStream);
        Assert.DoesNotContain("RUN_FINISHED", duplicateStream);
        response.Dispose();
        var scope = BrainScope.Create("owner", "cancel").Id;
        var conversation = brain.Get<IAgent>(AssistantConversations.Key(scope, "thread"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while ((await conversation.ReadConversation(ct)).ActiveRunId is not null) { await Task.Delay(100, deadline.Token); }
        Assert.Empty((await conversation.ReadConversation(ct)).Turns);
        Assert.Empty((await brain.Get<IWorkspace>(scope).Read()).Windows);

        // An interrupted run can be retried with its original ID. Keep one activity, update its
        // outcome, and include provider consumption already durably captured by the first attempt.
        JsonElement cancelled;
        do
        {
            using var page = JsonDocument.Parse(await brain.HttpClient.GetStringAsync("/brains/cancel/compute/usage", ct));
            cancelled = page.RootElement.GetProperty("items").Clone();
            if (cancelled.GetArrayLength() == 0) { await Task.Delay(100, deadline.Token); }
        } while (cancelled.GetArrayLength() == 0);
        Assert.Equal("Cancelled", Assert.Single(cancelled.EnumerateArray()).GetProperty("outcome").GetString());
        var usageId = ComputeUsageEndpoints.IntentId(scope, "thread", "cancel-run");
        await brain.Get<IIntentUsage>(usageId).RecordAsync(new(MeterKind.Chat, "OpenAI", "gpt-5.6-luna", 1234, 0, 0, 2, 1236, true, DateTimeOffset.UtcNow), ct);
        model.Delay = TimeSpan.Zero;
        using var retried = await brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        Assert.Contains("RUN_FINISHED", await retried.Content.ReadAsStringAsync(ct));
        using var history = JsonDocument.Parse(await brain.HttpClient.GetStringAsync("/brains/cancel/compute/usage", ct));
        var row = Assert.Single(history.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Succeeded", row.GetProperty("outcome").GetString());
        Assert.Equal(usageId, row.GetProperty("id").GetString());
        Assert.Contains(row.GetProperty("modelUsage").EnumerateArray(), entry => entry.GetProperty("inputTokens").GetInt64() == 1234);
        Assert.True(row.GetProperty("modelUsage").GetArrayLength() > 1);
    }

    [Fact(Timeout = 180_000)]
    public async Task RealModelProtocolOpensWindowAndReplayedRunDoesNotCallModelAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create().ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint)).StartAsync(ct);
        await using var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct));
        await connection.OpenAsync(ct);
        await using var seed = new NpgsqlCommand("CREATE TABLE leads (id int, company text, email text, active boolean); INSERT INTO leads VALUES (1, 'Real company', 'real@example.test', true)", connection);
        await seed.ExecuteNonQueryAsync(ct);
        var input = new { brainId = "agent", threadId = "thread", runId = "run", messages = new[] { new { role = "user", content = "Show active leads" } } };
        using var response = await brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        var stream = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_FINISHED", stream);
        Assert.DoesNotContain("RUN_ERROR", stream);
        Assert.Contains("TOOL_CALL_RESULT", stream);
        model.AssertCompleted();
        var usage = brain.Get<IIntentUsage>(ComputeUsageEndpoints.IntentId(BrainScope.Create("owner", "agent").Id, "thread", "run"));
        var recorded = await usage.ReadAsync(ct);
        Assert.NotEmpty(recorded.Entries);
        Assert.All(recorded.Entries, entry => Assert.True(entry.UsageReported, "The scripted model reports usage for every call."));
        Assert.All(recorded.Entries, entry => Assert.Equal(AiProvider.OpenAI.ToString(), entry.Provider));
        Assert.All(recorded.Entries, entry => Assert.Equal("gpt-5.6-luna", entry.Model));
        var recordedCount = recorded.Entries.Length;
        var workspace = brain.Get<IWorkspace>(BrainScope.Create("owner", "agent").Id);
        var state = await workspace.Read();
        Assert.True(Assert.Single(state.Windows).IsOpen);
        var count = model.Requests.Count;
        using var replay = await brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        Assert.Contains("RUN_FINISHED", await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(count, model.Requests.Count);
        Assert.Equal(recordedCount, (await usage.ReadAsync(ct)).Entries.Length);
        var history = await brain.HttpClient.GetFromJsonAsync<AgentConversationState>("/brains/agent/conversations/thread", ct);
        Assert.Equal(Assert.Single(state.Windows).Id, Assert.Single(Assert.Single(history!.Turns).ResultIds));
        var otherHistory = await brain.HttpClient.GetFromJsonAsync<AgentConversationState>("/brains/other/conversations/thread", ct);
        Assert.Empty(otherHistory!.Turns);
        using var next = await brain.HttpClient.PostAsJsonAsync("/agent", input with { runId = "next" }, ct);
        Assert.Contains("RUN_FINISHED", await next.Content.ReadAsStringAsync(ct));
        Assert.Contains("Opened Active leads", model.Requests[count].GetRawText());
    }
}
