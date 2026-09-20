using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Flutter;
using IntoChat.Tests.Fixtures;
using IntoChat.Workspace;
using Npgsql;
using DigitalBrain.AI.Conversations;
using IntoChat.Agent;

namespace IntoChat.Tests;

public sealed class AgentHttpFacts
{
    [Fact(Timeout = 240_000)]
    public async Task EmptyQueryIsRealAndFailedToolsNeverReportSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await AgentDataFixture.StartAsync(ct, web: false);
        async Task<string> Ask(string run)
        {
            using var response = await fixture.Brain.HttpClient.PostAsJsonAsync("/agent", new { workspaceId = "failures", threadId = "thread", runId = run, messages = new[] { new { role = "user", content = "Show leads" } } }, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        fixture.Model.Sql = "select id, company, email from leads where false";
        var emptyStream = await Ask("empty");
        Assert.True(emptyStream.Contains("RUN_FINISHED", StringComparison.Ordinal), emptyStream + "\n" + string.Join("\n", fixture.Model.Errors) + "\n" + string.Join("\n", fixture.Model.Requests.Select(r => r.GetRawText())));
        var workspace = fixture.Brain.Get<IWorkspace>(WorkspaceScope.Create("owner", "failures").Id);
        var window = Assert.Single((await workspace.Read()).Windows);
        Assert.Empty((await fixture.Brain.Get<DigitalBrain.Supabase.Tables.ISupabaseTable>(window.View.Id).Read(new(0, 25)))!.Rows);
        fixture.Model.Sql = "delete from leads";
        var invalid = await Ask("invalid");
        Assert.Contains("RUN_ERROR", invalid);
        Assert.DoesNotContain("RUN_FINISHED", invalid);
        fixture.Model.Sql = "select company from leads";
        fixture.Model.BeforeTable = fixture.RevokeDataAccess;
        var unavailable = await Ask("unavailable");
        Assert.Contains("RUN_ERROR", unavailable);
        Assert.DoesNotContain("RUN_FINISHED", unavailable);
        Assert.Single((await workspace.Read()).Windows);
    }

    [Fact(Timeout = 240_000)]
    public async Task DisconnectInterruptsRunAndRejectsConcurrentSubmission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await AgentDataFixture.StartAsync(ct, web: false);
        fixture.Model.Delay = TimeSpan.FromMinutes(1);
        var input = new { workspaceId = "cancel", threadId = "thread", runId = "cancel-run", messages = new[] { new { role = "user", content = "Show leads" } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/agent") { Content = JsonContent.Create(input) };
        using var response = await fixture.Brain.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await fixture.Model.ToolRequested.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        using var duplicate = await fixture.Brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        var duplicateStream = await duplicate.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_ERROR", duplicateStream);
        Assert.DoesNotContain("RUN_FINISHED", duplicateStream);
        response.Dispose();
        var scope = WorkspaceScope.Create("owner", "cancel").Id;
        var conversation = fixture.Brain.Get<IConversation>(ConversationCoordinator.Key(scope, "thread"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while ((await conversation.Read()).ActiveRunId is not null) { await Task.Delay(100, deadline.Token); }
        Assert.Empty((await conversation.Read()).Turns);
        Assert.Empty((await fixture.Brain.Get<IWorkspace>(scope).Read()).Windows);
    }

    [Fact(Timeout = 180_000)]
    public async Task RealModelProtocolOpensWindowAndReplayedRunDoesNotCallModelAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var deployment = await IntoChatTestDeployment.CreateAsync(ct);
        await using var brain = await deployment.Configure(E2ETest.For<Projects.IntoChat_AppHost>(), model.Endpoint, deployment.SolutionPath)
            .ConfigureModule<DigitalBrain.Flutter.FlutterModule>(flutter => flutter.WithoutHost()).StartAsync(ct);
        await using var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct));
        await connection.OpenAsync(ct);
        await using var seed = new NpgsqlCommand("CREATE TABLE leads (id int, company text, email text, active boolean); INSERT INTO leads VALUES (1, 'Real company', 'real@example.test', true)", connection);
        await seed.ExecuteNonQueryAsync(ct);
        var input = new { workspaceId = "agent", threadId = "thread", runId = "run", messages = new[] { new { role = "user", content = "Show active leads" } } };
        using var response = await brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        var stream = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_FINISHED", stream);
        Assert.DoesNotContain("RUN_ERROR", stream);
        Assert.Contains("TOOL_CALL_RESULT", stream);
        model.AssertCompleted();
        var workspace = brain.Get<IWorkspace>(WorkspaceScope.Create("owner", "agent").Id);
        var state = await workspace.Read();
        Assert.True(Assert.Single(state.Windows).IsOpen);
        var count = model.Requests.Count;
        using var replay = await brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        Assert.Contains("RUN_FINISHED", await replay.Content.ReadAsStringAsync(ct));
        Assert.Equal(count, model.Requests.Count);
        var history = await brain.HttpClient.GetFromJsonAsync<ConversationState>("/workspaces/agent/conversations/thread", ct);
        Assert.Equal(Assert.Single(state.Windows).Id, Assert.Single(Assert.Single(history!.Turns).ResultIds));
        var otherHistory = await brain.HttpClient.GetFromJsonAsync<ConversationState>("/workspaces/other/conversations/thread", ct);
        Assert.Empty(otherHistory!.Turns);
        using var next = await brain.HttpClient.PostAsJsonAsync("/agent", input with { runId = "next" }, ct);
        Assert.Contains("RUN_FINISHED", await next.Content.ReadAsStringAsync(ct));
        Assert.Contains("Opened Active leads", model.Requests[count].GetRawText());
    }
}
