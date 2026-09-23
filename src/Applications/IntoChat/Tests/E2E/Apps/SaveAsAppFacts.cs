using DigitalBrain.Apps;
using DigitalBrain.Apps.Manifests;
using DigitalBrain.Testing.Unit;

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
}
