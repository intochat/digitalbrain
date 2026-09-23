using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Manifests;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Testing.Unit;
using IntoChat.Agent;
using IntoChat.Apps;
using Microsoft.Extensions.AI;

namespace IntoChat.Tests.E2E.Apps;

// Save as app turns a window in a workspace into a declarative app. The app survives a restart
// (a fresh grain activation reads the persisted catalog) and uninstall states what data is kept.
public sealed class SaveAsAppFacts
{
    [Fact(Timeout = 120_000)]
    public async Task SavedFormAppSurvivesRestartAndUninstallStatesKeptData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        var catalog = brain.Get<IAppCatalog>("workspace-a");

        var installed = await catalog.SaveAsApp(new SaveAsAppRequest
        {
            Id = "intochat.saved-lead-form",
            Name = "New lead form",
            DescriptionForPeople = "Enter a new lead in one form.",
            DescriptionForModel = "Show the lead form and submit it atomically.",
            UiEntry = "app-saved-lead-form",
            Operations =
            [
                new AppOperation
                {
                    Name = "Submit",
                    DescriptionForModel = "Submit the lead form.",
                    InputTypeIds = new Dictionary<string, string> { ["company"] = "plain-text", ["email"] = "email" },
                    OutputTypeId = "reference",
                },
            ],
            ExamplePrompts = ["Add a new lead"],
        });
        Assert.Equal("intochat.saved-lead-form", installed.Manifest.Id);
        Assert.Equal(AppKind.Declarative, installed.Manifest.Kind);

        // A restart is a fresh activation over persisted state.
        await brain.DeactivateAsync(catalog, ct);
        var afterRestart = await catalog.Read("intochat.saved-lead-form");
        Assert.NotNull(afterRestart);
        Assert.Equal("New lead form", afterRestart!.Manifest.Name);
        Assert.Equal("app-saved-lead-form", afterRestart.Manifest.UiEntry);

        var outcome = await catalog.Uninstall("intochat.saved-lead-form");
        Assert.Contains("intochat.saved-lead-form:data", outcome.KeptData);
        Assert.Contains("intochat.saved-lead-form:install", outcome.RemovedData);
        Assert.Contains("app-saved-lead-form", outcome.RemovedData);
        Assert.Null(await catalog.Read("intochat.saved-lead-form"));
        Assert.Empty(await catalog.List());
    }

    [Fact(Timeout = 120_000)]
    public async Task SavedTableAppInstallsAlongsideAFormAndRollsBackByVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        var catalog = brain.Get<IAppCatalog>("workspace-b");

        await catalog.SaveAsApp(Table());
        var second = await catalog.SaveAsApp(Table());
        Assert.Equal("1.0.1", second.Manifest.Version);
        Assert.Single(await catalog.List());

        var rolledBack = await catalog.Rollback("intochat.saved-leads-view");
        Assert.Equal("1.0.0", rolledBack.Manifest.Version);
        Assert.Equal("1.0.0", (await catalog.Read("intochat.saved-leads-view"))!.Manifest.Version);
    }

    private static SaveAsAppRequest Table() => new()
    {
        Id = "intochat.saved-leads-view",
        Name = "Active leads",
        DescriptionForPeople = "A live view of active leads.",
        DescriptionForModel = "Read and refine the active leads live table.",
        Operations =
        [
            new AppOperation
            {
                Name = "Read",
                DescriptionForModel = "Read the leads live table.",
                ReadOnly = true,
                OutputTypeId = "reference",
            },
        ],
    };

    [Fact(Timeout = 120_000)]
    public async Task AssistantToolSavesTheOpenFormAndReopensItAfterRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().WithModule<AppsModule>().StartAsync(ct);
        const string scope = "workspace-save";
        var formId = scope + "/apps/forms/lead";
        await brain.Get<IForm>(formId).Define(new("Lead intake",
        [
            new("company", "Company", FieldKind.PlainText, true),
            new("email", "Email", FieldKind.Email),
        ]));
        var workspace = brain.Get<IWorkspace>(scope);
        await workspace.OpenSurface(new("open-form", formId, "Lead intake", new("surface", formId + "/surface"), 0));

        var tools = new WorkspaceAppTools(new WorkspaceAppService(brain)).Create(() => new AgentToolContext(scope, "run", "call"));
        var result = await tools.Single(tool => tool.Name == "save_as_app")
            .InvokeAsync(new AIFunctionArguments { ["name"] = "Lead Intake" }, ct);
        Assert.NotNull(result);

        var catalog = brain.Get<IAppCatalog>(scope);
        var saved = await catalog.Read("intochat.saved-lead-intake");
        Assert.NotNull(saved);
        Assert.Equal(AppKind.Declarative, saved!.Manifest.Kind);
        Assert.Contains(saved.Manifest.Windows, window => window.WindowId == formId && window.Kind == "surface");

        // A restart is a fresh activation over persisted state.
        await brain.DeactivateAsync(catalog, ct);
        var afterRestart = await catalog.Read("intochat.saved-lead-intake");
        Assert.NotNull(afterRestart);
        Assert.Single(afterRestart!.Manifest.Windows);

        await workspace.Close(formId, (await workspace.Read()).Revision);
        var reopened = await new WorkspaceAppService(brain).OpenAsync(scope, "intochat.saved-lead-intake", ct);
        Assert.Contains(reopened.Windows, window => window.Id == formId && window.IsOpen);
    }

    [Fact(Timeout = 120_000)]
    public async Task DiscoveryFindsASavedAppByItsDescription()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<FlutterModule>().WithModule<AppsModule>().WithModule<DiscoveryModule>().StartAsync(ct);
        const string scope = "workspace-discovery";
        var formId = scope + "/apps/forms/intake";
        await brain.Get<IForm>(formId).Define(new("Intake", [new("company", "Company", FieldKind.PlainText, true)]));
        await brain.Get<IWorkspace>(scope)
            .OpenSurface(new("open", formId, "Vendor onboarding intake", new("surface", formId + "/surface"), 0));

        await new WorkspaceAppService(brain).SaveAsync(scope, "Vendor Onboarding Intake", ct);

        var result = await brain.Get<ICapabilityCatalog>("catalog").Search("vendor onboarding intake", scope, 5);

        Assert.Contains(result.Hits, hit => hit.Id.StartsWith("intochat.saved-vendor-onboarding-intake", StringComparison.Ordinal));
    }
}
