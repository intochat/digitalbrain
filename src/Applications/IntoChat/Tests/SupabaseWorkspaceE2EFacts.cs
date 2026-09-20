using IntoChat.Tests.Fixtures;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.AI.Conversations;

namespace IntoChat.Tests;

public sealed class SupabaseWorkspaceE2EFacts
{
    private static async Task EnterText(ILocator input, string text)
    {
        await input.ClickAsync();
        // Flutter installs editing handlers in the semantics frame after focus.
        // Synchronize with rendering; a DOM focus event alone is not readiness.
        await input.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await input.FillAsync(text);
        await Assertions.Expect(input).ToHaveValueAsync(text);
    }
    [Fact(Timeout = 300_000)]
    public async Task EmptyAndFailedQueriesShowStateWithoutInventedRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await AgentDataFixture.StartAsync(ct);
        fixture.Model.Sql = "select id, company, email from leads where false";
        await using var browser = await fixture.Brain.OpenBrowserAsync(ct);
        var page = browser.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        await page.GetByRole(AriaRole.Button, new() { Name = "New IntoChat project" }).ClickAsync();
        await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Project name (optional)" }), "Query failures");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start project", Exact = true }).ClickAsync();
        async Task Send(string text)
        {
            await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), text);
            await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        }
        await Send("Show active leads");
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window.GetByText("No rows match these filters.", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 60000 });
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Send" })).ToBeVisibleAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Model.BeforeTable = async token => { started.TrySetResult(); await blocked.Task.WaitAsync(token); };
        var cancelledRequest = await page.RunAndWaitForRequestAsync(() => Send("Cancel a delayed query"), request => request.Url.EndsWith("/agent", StringComparison.Ordinal) && request.Method == "POST");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        await page.GetByRole(AriaRole.Button, new() { Name = "Stop response" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Response stopped.", new() { Exact = true })).ToBeVisibleAsync();
        using (var sent = JsonDocument.Parse(cancelledRequest.PostData!))
        {
            var workspace = sent.RootElement.GetProperty("workspaceId").GetString();
            var thread = sent.RootElement.GetProperty("threadId").GetString();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while ((await fixture.Brain.HttpClient.GetFromJsonAsync<ConversationState>($"/workspaces/{workspace}/conversations/{thread}", deadline.Token))!.ActiveRunId is not null)
                { await Task.Delay(100, deadline.Token); }
        }
        blocked.TrySetResult();
        fixture.Model.BeforeTable = null;
        fixture.Model.Sql = "delete from leads";
        var failure = page.GetByText("The request could not be completed. Check the data connection or try again.", new() { Exact = true });
        async Task ExpectFailure(string text)
        {
            var response = await page.RunAndWaitForResponseAsync(() => Send(text), response => response.Url.EndsWith("/agent", StringComparison.Ordinal) && response.Request.Method == "POST");
            await response.FinishedAsync().WaitAsync(TimeSpan.FromSeconds(60), ct);
            await Assertions.Expect(failure).ToBeVisibleAsync();
        }
        await ExpectFailure("Try an invalid query");
        await Assertions.Expect(window).ToHaveCountAsync(1);
        fixture.Model.Sql = "select company from leads";
        fixture.Model.BeforeTable = fixture.StopDatabase;
        await ExpectFailure("Read with unavailable data access");
        await Assertions.Expect(window).ToHaveCountAsync(1);
        var refresh = await page.RunAndWaitForResponseAsync(
            () => window.GetByRole(AriaRole.Button, new() { Name = "Refresh table", Exact = true }).ClickAsync(),
            response => response.Url.Contains("/tables/", StringComparison.Ordinal) && response.Request.Method == "GET");
        await refresh.FinishedAsync().WaitAsync(TimeSpan.FromSeconds(60), ct);
        await Assertions.Expect(window.GetByText("Showing previous data.", new() { Exact = false })).ToBeVisibleAsync();
    }

    public static bool LiveEnabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_LIVE_MODEL") == "1";
    [Fact(Timeout = 300_000, SkipUnless = nameof(LiveEnabled), Skip = "Set DIGITALBRAIN_E2E_LIVE_MODEL=1 and provide model credentials to run paid live verification.")]
    [Trait("Category", "LiveModel")]
    public async Task LiveModelOpensAnInteractiveSupabaseTable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await AgentDataFixture.StartAsync(ct, live: true);
        await using var browser = await fixture.Brain.OpenBrowserAsync(ct);
        var page = browser.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        await page.GetByRole(AriaRole.Button, new() { Name = "New IntoChat project" }).ClickAsync();
        await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Project name (optional)" }), "Live data workspace");
        await page.GetByRole(AriaRole.Button, new() { Name = "Start project", Exact = true }).ClickAsync();
        await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), "Discover Supabase schema and show all active leads in an interactive table with id, company and email ordered by id. Title it Active leads.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window).ToBeVisibleAsync(new() { Timeout = 120000 });
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Inactive control", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(window.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true })).ToBeVisibleAsync();
    }
    [Fact(Timeout = 300_000)]
    public async Task AskOpensLiveTableWithServerFilteringAndRestoresWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await AgentDataFixture.StartAsync(ct);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Model.BeforeTable = token => release.Task.WaitAsync(token);
        await using var browser = await fixture.Brain.OpenBrowserAsync(ct);
        var page = browser.Page;
        try
        {
            await page.SetViewportSizeAsync(1600, 1000);
            await page.GetByRole(AriaRole.Button, new() { Name = "New IntoChat project" }).ClickAsync();
            await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Project name (optional)" }), "Data workspace");
            await page.GetByRole(AriaRole.Button, new() { Name = "Start project", Exact = true }).ClickAsync();
            await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), "Show the active leads from Supabase with company and email");
            await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
            var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
            await fixture.Model.ToolRequested.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await page.GetByRole(AriaRole.Button, new() { Name = "IntoChat", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "New IntoChat project" }).ClickAsync();
            await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Project name (optional)" }), "Other workspace");
            await page.GetByRole(AriaRole.Button, new() { Name = "Start project", Exact = true }).ClickAsync();
            release.TrySetResult();
            await fixture.Model.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await Assertions.Expect(window).ToHaveCountAsync(0);
            await page.GetByRole(AriaRole.Button, new() { Name = "IntoChat", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Data workspace" }).ClickAsync();
            await Assertions.Expect(window).ToBeVisibleAsync(new() { Timeout = 60000 });
            await Assertions.Expect(window.GetByRole(AriaRole.Columnheader)).ToHaveCountAsync(3);
            foreach (var column in new[] { "id", "company", "email" })
                { await Assertions.Expect(window.GetByRole(AriaRole.Columnheader, new() { Name = column, Exact = true })).ToBeVisibleAsync(); }
            await Assertions.Expect(window.GetByText("1–25 of 60", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(window.GetByText("Inactive control", new() { Exact = true })).ToHaveCountAsync(0);
            await window.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
            await Assertions.Expect(window.GetByText("26–50 of 60", new() { Exact = true })).ToBeVisibleAsync();
            await window.GetByRole(AriaRole.Button, new() { Name = "Previous page", Exact = true }).ClickAsync();
            await window.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Filter column" }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "company", Exact = true }).ClickAsync();
            await EnterText(page.GetByRole(AriaRole.Textbox, new() { Name = "Filter value", Exact = true }), fixture.Marker);
            await page.GetByRole(AriaRole.Button, new() { Name = "Apply filter", Exact = true }).ClickAsync();
            await Assertions.Expect(window.GetByText(fixture.Marker, new() { Exact = true })).ToBeVisibleAsync();
            await page.ReloadAsync();
            await Assertions.Expect(window.GetByText(fixture.Marker, new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 60000 });
            await Assertions.Expect(window).ToHaveCountAsync(1);
            await page.GetByRole(AriaRole.Button, new() { Name = "Close editor", Exact = true }).ClickAsync();
            await Assertions.Expect(window).ToHaveCountAsync(0);
            await page.GetByRole(AriaRole.Button, new() { Name = "Open Active leads", Exact = true }).ClickAsync();
            await Assertions.Expect(window).ToHaveCountAsync(1);
            await window.GetByRole(AriaRole.Button, new() { Name = "Clear filters", Exact = true }).ClickAsync();
            await Assertions.Expect(window.GetByText("1–25 of 60", new() { Exact = true })).ToBeVisibleAsync();
            await page.RunAndWaitForResponseAsync(() => window.GetByRole(AriaRole.Columnheader, new() { Name = "id", Exact = true }).ClickAsync(), response => response.Url.EndsWith("/view", StringComparison.Ordinal) && response.Request.Method == "POST");
            await page.RunAndWaitForResponseAsync(() => window.GetByRole(AriaRole.Columnheader, new() { Name = "id", Exact = true }).ClickAsync(), response => response.Url.EndsWith("/view", StringComparison.Ordinal) && response.Request.Method == "POST");
            await Assertions.Expect(window.GetByText("Company 60", new() { Exact = true })).ToBeVisibleAsync();
            fixture.Model.AssertCompleted();
            await page.ScreenshotAsync(new() { Path = "workspace-browser-success.png", FullPage = true });
        }
        catch
        {
            await File.WriteAllTextAsync("workspace-browser-dom.log", await page.Locator("body").InnerTextAsync(), ct);
            await File.WriteAllTextAsync("workspace-browser-aria.log", await page.Locator("body").AriaSnapshotAsync(), ct);
            await page.ScreenshotAsync(new() { Path = "workspace-browser-failure.png", FullPage = true });
            throw;
        }
    }
}
