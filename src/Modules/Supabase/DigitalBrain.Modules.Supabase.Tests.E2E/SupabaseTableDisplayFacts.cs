using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;
using Microsoft.Playwright;
using DigitalBrain.Sdk.Identity;

namespace DigitalBrain.Modules.Supabase.Tests.E2E;

public sealed class SupabaseTableDisplayFacts(ReferenceBrainFixture host) : BrainFact(host)
{
    [Fact(Timeout = 300_000)]
    public async Task DatabaseTableAppearsAndFilterFindsRowsBeyondTheFirstPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Brain;
        const string marker = "Beyond the first page";
        await LeadData.SeedAsync(brain, marker, ct);
        var page = await OpenPageAsync(ct);
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Display data workspace");
        var workspace = brain.Get<IWorkspace>(BrainScope.Create("owner", projectId).Id);
        var table = brain.Get<ISupabaseTable>("active-leads");
        var snapshot = await table.CreateFromQuery(new("Active leads", "select id, company, email from leads where active order by id"));
        await workspace.Open(new("show-leads", "leads-window", "Active leads", WindowReference.Table(snapshot.Id), (await workspace.Read()).Revision));

        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByRole(AriaRole.Columnheader)).ToHaveCountAsync(3);
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Inactive control", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(window.GetByText("1–25 of 60", new() { Exact = true })).ToBeVisibleAsync();

        // One real filter gesture proves the product adapter serves data beyond the cached page.
        await window.GetByRole(AriaRole.Button, new() { Name = "Filter", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Filter column" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "company", Exact = true }).ClickAsync();
        await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Filter value", Exact = true }), marker);
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filter", Exact = true }).ClickAsync();
        await Assertions.Expect(window.GetByText(marker, new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Fact(Timeout = 300_000)]
    public async Task EmptyResultsAndUnavailableSourceAreShownWithoutInventedRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Brain;
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        var page = await OpenPageAsync(ct);
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Display unavailable data");
        var workspace = brain.Get<IWorkspace>(BrainScope.Create("owner", projectId).Id);
        var table = brain.Get<ISupabaseTable>("empty-leads");
        var snapshot = await table.CreateFromQuery(new("Empty leads", "select id, company, email from leads where false"));
        await workspace.Open(new("show-empty", "empty-window", "Empty leads", WindowReference.Table(snapshot.Id), (await workspace.Read()).Revision));
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Empty leads", Exact = true });
        await Assertions.Expect(window.GetByText("No rows match these filters.", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToHaveCountAsync(0);

        await LeadData.DropAsync(brain, ct);
        await window.GetByRole(AriaRole.Button, new() { Name = "Refresh table", Exact = true }).ClickAsync();
        await Assertions.Expect(window.GetByText("Showing previous data.", new() { Exact = false })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToHaveCountAsync(0);
    }
}
