using System.Text.Json;
using Aspire.Hosting.Testing;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using IntoChat.Agent;
using IntoChat.Tests.E2E.Workspace;
using IntoChat.Workspace;
using Microsoft.Playwright;
using Npgsql;

namespace IntoChat.Tests.E2E.Agent;

public sealed class AgentTableJourneyFacts
{
    [Fact(Timeout = 300_000)]
    public async Task UserRequestOpensTableAndRecoversAfterCancellationAndFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        // Real models commonly emit a final statement terminator.
        model.Sql += ";";
        model.RepairSql = model.Sql;
        model.Sql = "select * from wide_customers order by id;";
        model.ExpectedValidationError = "it returns 62";
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp())
            .StartAsync(ct);
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        await LeadData.CreateWideCustomersAsync(brain, ct);
        var page = brain.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Agent workspace");
        await SendAsync(page, "Show active leads from Supabase");
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Inactive control", new() { Exact = true })).ToHaveCountAsync(0);
        await model.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        model.AssertCompleted();
        Assert.Equal(1, model.RepairCount);
        model.Sql = model.RepairSql;
        model.RepairSql = null;
        model.ExpectedValidationError = null;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BeforeTable = async token => { started.TrySetResult(); await release.Task.WaitAsync(token); };
        try
        {
            var request = await page.RunAndWaitForRequestAsync(() => SendAsync(page, "Cancel a delayed request"),
                request => request.Url.EndsWith("/agent", StringComparison.Ordinal) && request.Method == "POST");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await page.GetByRole(AriaRole.Button, new() { Name = "Stop response" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Response stopped.", new() { Exact = true })).ToBeVisibleAsync();
            using var input = JsonDocument.Parse(request.PostData!);
            var threadId = input.RootElement.GetProperty("threadId").GetString()!;
            var conversation = brain.Get<IAgent>(AgentEndpoints.ConversationKey(WorkspaceScope.Create("owner", projectId).Id, threadId));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while ((await conversation.ReadConversation(ct)).ActiveRunId is not null) { await Task.Delay(100, deadline.Token); }
            Assert.StartsWith("table-", Assert.Single(Assert.Single((await conversation.ReadConversation(ct)).Turns).ResultIds));
            await Assertions.Expect(window).ToHaveCountAsync(1);
        }
        finally { release.TrySetResult(); }

        model.BeforeTable = null;
        model.Sql = "select missing_column from leads";
        var failure = page.GetByText("The table could not be opened: PostgreSQL refused the query (SQLSTATE 42703). Check table/column names, permissions and read-only SQL; narrow expensive queries.", new() { Exact = true });
        await SendAsync(page, "Try an invalid query");
        await Assertions.Expect(failure).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(window).ToHaveCountAsync(1);

        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BeforeTable = async token => { retryStarted.TrySetResult(); await retryRelease.Task.WaitAsync(token); };
        try
        {
            await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), "Retry the query");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true })).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await Assertions.Expect(failure).ToBeHiddenAsync();
            retryRelease.TrySetResult();
            await Assertions.Expect(failure).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), "Another request");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true })).ToBeEnabledAsync();
            await Assertions.Expect(window).ToHaveCountAsync(1);
        }
        finally { retryRelease.TrySetResult(); }
    }

    [Fact(Timeout = 300_000)]
    public async Task RefineAndCountStayInTheSameWindowWithoutReturningRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        model.Sql = "select id, company, city from customers order by id";
        model.RefineColumn = "city";
        model.RefineOperator = "eq";
        model.RefineValue = "London";
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp())
            .StartAsync(ct);
        // The scripted model discovers schema against public.leads, so it must exist.
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        await using (var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct)))
        {
            await connection.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                "CREATE TABLE customers (id int PRIMARY KEY, company text, city text); " +
                "INSERT INTO customers SELECT n, 'Customer ' || n, CASE WHEN n % 10 = 0 THEN 'London' ELSE 'Berlin' END FROM generate_series(1,60) n",
                connection);
            await seed.ExecuteNonQueryAsync(ct);
        }
        var page = brain.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Refine workspace");
        var workspace = brain.Get<IWorkspace>(WorkspaceScope.Create("owner", projectId).Id);

        await SendAsync(page, "Show all customers from Supabase");
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window.GetByText("Customer 10", new() { Exact = true })).ToBeVisibleAsync();

        // "Only London" refines the existing window; it must not open a second one.
        await SendAsync(page, "Only London");
        await Assertions.Expect(page.GetByText("Refined the same window to London.", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(window).ToHaveCountAsync(1);
        var tableId = Assert.Single((await workspace.Read()).Windows).Reference.NeuronId;
        var refined = await brain.Get<DigitalBrain.Supabase.Tables.ISupabaseTable>(tableId).Read(new(0, 25));
        Assert.Equal(60, refined!.TotalRows);
        Assert.Equal(6, refined.FilteredRows);
        Assert.Equal("city", Assert.Single(refined.Filters).ColumnId);

        // Refresh reads the saved view again, so the same window now shows only London rows.
        await window.GetByRole(AriaRole.Button, new() { Name = "Refresh table", Exact = true }).ClickAsync();
        await Assertions.Expect(window.GetByText("Customer 10", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Customer 11", new() { Exact = true })).ToHaveCountAsync(0);

        // "How many?" is answered from a count aggregate; the read returns no row values.
        await SendAsync(page, "How many?");
        await Assertions.Expect(page.GetByText("6 in London", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 60_000 });
        Assert.Equal(6, model.LastReadFilteredRows);
        Assert.Equal("6", model.LastReadAggregate);
        await Assertions.Expect(window).ToHaveCountAsync(1);
        model.AssertNoProtocolErrors();
    }

    private static async Task SendAsync(IPage page, string text)
    {
        await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), text);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
    }
}