using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;
using IntoChat.Workspace;
using Microsoft.Playwright;

namespace IntoChat.Tests.E2E.Workspace;

public sealed class WorkspaceRestoreFacts
{
    [Fact(Timeout = 300_000)]
    public async Task RemoteWindowStaysInItsWorkspaceAndFilteredViewSurvivesReloadAndReopen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp()).StartAsync(ct);
        const string marker = "Restored filtered row";
        await LeadData.SeedAsync(brain, marker, ct);
        var page = brain.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Data workspace");
        await WorkspaceBrowser.CreateProjectAsync(page, "Other workspace");

        var table = brain.Get<ISupabaseTable>("restored-leads");
        var snapshot = await table.CreateFromQuery(new("Active leads", "select id, company, email from leads where active order by id"));
        var company = snapshot.Columns.Single(column => column.Label == "company");
        await table.UpdateView(new(snapshot.Revision, [new(company.Id, "eq", JsonSerializer.Serialize(marker))], null, snapshot.VisibleColumns));
        var workspace = brain.Get<IWorkspace>(WorkspaceScope.Create("owner", projectId).Id);
        await workspace.Open(new("show-leads", "leads-window", "Active leads", new(snapshot.Id), (await workspace.Read()).Revision));

        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Workspaces") }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Data workspace", Exact = true }).ClickAsync();
        await Assertions.Expect(window.GetByText(marker, new() { Exact = true })).ToBeVisibleAsync();

        await page.ReloadAsync();
        await Assertions.Expect(window.GetByText(marker, new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window).ToHaveCountAsync(1);
        await page.GetByRole(AriaRole.Button, new() { Name = "Close editor", Exact = true }).ClickAsync();
        await Assertions.Expect(window).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Workspaces") }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Saved work", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Alertdialog)
            .GetByRole(AriaRole.Button, new() { Name = "Active leads" }).ClickAsync();
        await Assertions.Expect(window).ToHaveCountAsync(1);
        await Assertions.Expect(window.GetByText(marker, new() { Exact = true })).ToBeVisibleAsync();
    }
}
