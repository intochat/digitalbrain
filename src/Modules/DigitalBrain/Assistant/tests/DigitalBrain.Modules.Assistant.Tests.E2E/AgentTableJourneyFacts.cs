using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Assistant;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Testing.E2E.Workspace;
using Microsoft.Playwright;
using Npgsql;

namespace DigitalBrain.Modules.Assistant.Tests.E2E;

public sealed class AgentTableJourneyFacts(ReferenceBrainFixture host) : BrainFact(host)
{
    [Fact(Timeout = 300_000)]
    public async Task UserRequestOpensTableAndRecoversAfterCancellationAndFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var model = Model;
        // Real models commonly emit a final statement terminator.
        model.Sql += ";";
        model.RepairSql = model.Sql;
        model.Sql = "select * from wide_customers order by id;";
        model.ExpectedValidationError = "it returns 62";
        var brain = Brain;
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        await LeadData.CreateWideCustomersAsync(brain, ct);
        var page = await OpenPageAsync(ct);
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Agent journey workspace");
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
            await SendAsync(page, "Cancel a delayed request");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            // The shell chat drives the assistant app; Stop cancels the running turn. Grain-side
            // cancellation semantics are covered by AgentWorkflowFacts over the /agent route;
            // here the journey proves the UI settles and the cancelled tool opens no window.
            await page.GetByRole(AriaRole.Button, new() { Name = "Stop", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Stop", Exact = true }))
                .ToBeDisabledAsync(new() { Timeout = 30_000 });
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
        var model = Model;
        model.Sql = "select id, company, city from customers order by id";
        model.RefineColumn = "city";
        model.RefineOperator = "eq";
        model.RefineValue = "London";
        var brain = Brain;
        // The scripted model discovers schema against public.leads, so it must exist.
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        await LeadData.CreateCustomersAsync(brain, ct);
        var page = await OpenPageAsync(ct);
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Agent refine workspace");
        var workspace = brain.Get<IWorkspace>(BrainScope.Create("owner", projectId).Id);

        await SendAsync(page, "Show all customers from Supabase");
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window.GetByText("Customer 10", new() { Exact = true })).ToBeVisibleAsync();

        // "Only London" refines the existing window; it must not open a second one.
        await SendAsync(page, "Only London");
        // The reply renders inside the conversation transcript; match its text, not the group name.
        await Assertions.Expect(page.GetByText("Refined the same window to London.")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(window).ToHaveCountAsync(1);
        var tableId = Assert.Single((await workspace.Read()).Windows).Reference.NeuronId;
        var refined = await brain.Get<DigitalBrain.Supabase.Tables.ISupabaseTable>(tableId).Read(new(0, 25));
        Assert.Equal(60, refined!.TotalRows);
        Assert.Equal(6, refined.FilteredRows);
        Assert.Equal("city", Assert.Single(refined.Filters).ColumnId);

        // The refined view is durable on the table neuron; Refresh shows it in the window:
        // London rows only, no Berlin row.
        await window.GetByRole(AriaRole.Button, new() { Name = "Refresh table", Exact = true }).ClickAsync();
        await Assertions.Expect(window.GetByText("Customer 10", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Customer 11", new() { Exact = true })).ToHaveCountAsync(0);

        // "How many?" is answered from a count aggregate; the read returns no row values.
        await SendAsync(page, "How many?");
        await Assertions.Expect(page.GetByText("6 in London")).ToBeVisibleAsync(new() { Timeout = 60_000 });
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
