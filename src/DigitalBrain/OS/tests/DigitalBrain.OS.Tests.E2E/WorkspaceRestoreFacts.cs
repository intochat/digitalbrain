using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Supabase.Tables;
using Microsoft.Playwright;

namespace DigitalBrain.OS.Tests.E2E;

public sealed class WorkspaceRestoreFacts(ReferenceBrainFixture host) : BrainFact(host)
{
    [Fact(Timeout = 300_000)]
    public async Task RemoteWindowStaysInItsWorkspaceAndFilteredViewSurvivesReloadAndReopen()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Brain;
        const string marker = "Restored filtered row";
        await LeadData.SeedAsync(brain, marker, ct);
        var page = await OpenPageAsync(ct);
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Restore data workspace");
        await WorkspaceBrowser.CreateProjectAsync(page, "Restore other workspace");

        var table = brain.Get<ISupabaseTable>("restored-leads");
        var snapshot = await table.CreateFromQuery(new("Active leads", "select id, company, email from leads where active order by id"));
        var company = snapshot.Columns.Single(column => column.Label == "company");
        await table.UpdateView(new(snapshot.Revision, [new(company.Id, "eq", JsonSerializer.Serialize(marker))], null, snapshot.VisibleColumns));
        var workspace = brain.Get<IWorkspace>(BrainScope.Create("owner", projectId).Id);
        await workspace.Open(new("show-leads", "leads-window", "Active leads", WindowReference.Table(snapshot.Id), (await workspace.Read()).Revision));

        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Workspaces") }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Restore data workspace", Exact = true }).ClickAsync();
        await Assertions.Expect(window.GetByText(marker, new() { Exact = true })).ToBeVisibleAsync();

        // A reload starts with a clean dock by design; the saved window is reopened explicitly
        // and still carries its filtered view.
        await page.ReloadAsync();
        await Assertions.Expect(window).ToHaveCountAsync(0);
        await OpenSavedWorkAsync(page);
        await Assertions.Expect(window).ToHaveCountAsync(1);
        await Assertions.Expect(window.GetByText(marker, new() { Exact = true })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Close editor", Exact = true }).ClickAsync();
        await Assertions.Expect(window).ToHaveCountAsync(0);
        await OpenSavedWorkAsync(page);
        await Assertions.Expect(window).ToHaveCountAsync(1);
        await Assertions.Expect(window.GetByText(marker, new() { Exact = true })).ToBeVisibleAsync();
    }

    private static async Task OpenSavedWorkAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Workspaces") }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Saved work", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Alertdialog)
            .GetByRole(AriaRole.Button, new() { Name = "Active leads" }).ClickAsync();
    }
}
