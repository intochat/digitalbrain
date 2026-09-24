using DigitalBrain.Apps;
using DigitalBrain.Core;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class WorkspaceAppFacts
{
    [Fact]
    public void MalformedPublishedManifestReturnsValidationErrors()
    {
        var manifest = Greeting();
        Assert.Throws<AppManifestException>(() => ManifestValidator.Validate(manifest with { Version = null! }));
        Assert.Throws<AppManifestException>(() => ManifestValidator.Validate(manifest with { Operations = null! }));
        Assert.Throws<AppManifestException>(() => ManifestValidator.Validate(manifest with { Permissions = null! }));
        Assert.Throws<AppManifestException>(() => ManifestValidator.Validate(manifest with { Windows = null! }));
    }

    [Fact]
    public async Task LongLivedAppsKeepRunningAndReplayEarlyOperationsAfterMoreThan1024Runs()
    {
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        var app = brain.Get<IWorkspaceApp>("long-lived");
        await app.Install(Greeting(), new Dictionary<string, string>());
        var first = new AppDispatch(Guid.NewGuid(), "run", "first");
        var original = await app.Dispatch(first);
        for (var index = 0; index < 1025; index++) { await app.Dispatch(new(Guid.NewGuid(), "run", index.ToString(System.Globalization.CultureInfo.InvariantCulture))); }
        Assert.Equal("HELLO 1024", (await app.Read()).Output);
        Assert.Equal(original.Revision, (await app.Dispatch(first)).Revision);
        Assert.Equal("HELLO 1024", (await app.Read()).Output);
    }

    [Fact]
    public void ExponentialFanoutIsRejectedBeforeInstallation()
    {
        var graph = new AppComposition
        {
            Parts = Enumerable.Range(0, 10).Select(index => new AppPart("p" + index, "text.identity", new Dictionary<string, string>())).ToArray(),
            Bindings = new[] { new AppBinding("$app", "run", "p0") }.Concat(
                Enumerable.Range(0, 10).SelectMany(source => Enumerable.Range(source + 1, 9 - source)
                    .Select(target => new AppBinding("p" + source, "completed", "p" + target)))).ToArray(),
        };
        Assert.Throws<AppManifestException>(() => AppCompositionValidation.Validate(graph));
    }

    [Fact]
    public async Task WorkspaceActivationIsRecoverableAndReachesLateInstalledAppsOnce()
    {
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        var service = new WorkspaceApps(brain);
        await service.ActivateWorkspace("workspace");
        var manifest = Greeting();
        manifest = manifest with { Composition = manifest.Composition! with
        {
            Activation = AppActivation.WithWorkspace,
            Bindings = [new("$brain", "activated", "greet"), new("$app", "run", "greet"), new("greet", "completed", "upper")],
        } };
        var started = await service.Install("workspace", manifest, new Dictionary<string, string>());
        Assert.Equal("HELLO ", started.Output);
        Assert.Equal(AppStatus.Running, started.Status);
        Assert.Equal(started.Revision, Assert.Single(await service.ActivateWorkspace("workspace")).Revision);
    }

    [Fact]
    public async Task InstalledCompositionRunsWithIndependentWorkspaceConfigurationAndState()
    {
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        var alice = brain.Get<IWorkspaceApp>("alice/research");
        var bob = brain.Get<IWorkspaceApp>("bob/research");
        var manifest = Greeting();
        await alice.Install(manifest, new Dictionary<string, string> { ["prefix"] = "Alice: " });
        await bob.Install(manifest, new Dictionary<string, string> { ["prefix"] = "Bob: " });
        var operation = Guid.NewGuid();
        var first = await alice.Dispatch(new(operation, "run", "hello"));
        Assert.Equal("ALICE: HELLO", first.Output);
        Assert.Equal("BOB: WORLD", (await bob.Dispatch(new(Guid.NewGuid(), "run", "world"))).Output);
        Assert.Equal(first.Revision, (await alice.Dispatch(new(operation, "run", "hello"))).Revision);
        Assert.Equal("ALICE: HELLO", (await alice.Read()).Output);
        await bob.Install(manifest, new Dictionary<string, string> { ["prefix"] = "reset" });
        Assert.Equal("Bob: ", (await bob.Read()).Configuration["prefix"]);
        Assert.Equal("BOB: WORLD", (await bob.Read()).Output);
    }

    [Fact]
    public async Task StoppingAnAppPreventsDispatchUntilExplicitActivationAndActivationIsIdempotent()
    {
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        var app = brain.Get<IWorkspaceApp>("workspace/lifecycle");
        await app.Install(Greeting(), new Dictionary<string, string>());
        var active = await app.Activate();
        Assert.Equal(active.Revision, (await app.Activate()).Revision);
        await app.Deactivate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Dispatch(new(Guid.NewGuid(), "run", "hello")));
        await app.Activate();
        Assert.Equal("HELLO WORLD", (await app.Dispatch(new(Guid.NewGuid(), "run", "world"))).Output);
    }

    [Fact]
    public async Task InvalidGraphAndConfigurationAreRejectedWithoutChangingInstalledState()
    {
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        var app = brain.Get<IWorkspaceApp>("workspace/validation");
        var manifest = Greeting();
        await Assert.ThrowsAsync<AppManifestException>(() => app.Install(manifest with
        {
            Composition = manifest.Composition! with { RequiredModules = ["missing-module"] },
        }, new Dictionary<string, string>()));
        await Assert.ThrowsAsync<AppManifestException>(() => app.Install(manifest with
        {
            Composition = manifest.Composition! with
            {
                Bindings = [new("$app", "run", "greet"), new("greet", "completed", "upper"), new("upper", "completed", "greet")],
            },
        }, new Dictionary<string, string>()));
        await app.Install(manifest, new Dictionary<string, string>());
        await Assert.ThrowsAsync<AppManifestException>(() => app.Configure(new Dictionary<string, string> { ["unknown"] = "x" }));
        Assert.Equal("Hello ", (await app.Read()).Configuration["prefix"]);
    }

    internal static AppManifest Greeting() => new()
    {
        Id = "alice/greeting", Publisher = "alice", Version = "1.0.0", Kind = AppKind.Declarative,
        Name = "Greeting", DescriptionForPeople = "Compose a greeting", DescriptionForModel = "Compose a greeting",
        Operations = [new() { Name = "run", DescriptionForModel = "Run the greeting", OutputTypeId = "plain-text" }],
        Scenarios = [new() { Name = "Greeting", Given = "input", When = "run", Then = "a greeting" }],
        Composition = new()
        {
            Defaults = new Dictionary<string, string> { ["prefix"] = "Hello " },
            Parts = [new("greet", "text.prefix", new Dictionary<string, string> { ["configurationKey"] = "prefix" }), new("upper", "text.uppercase", new Dictionary<string, string>())],
            Bindings = [new("$app", "run", "greet"), new("greet", "completed", "upper")],
        },
    };
}
