using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Microsoft.CSharp;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Workspace;

public sealed class AppFacts
{
    private static readonly PackageId Researcher = PackageId.Parse("alice/researcher");

    [Fact]
    public async Task InstallationRequiresAccountBindingsAndPassesOnlySelectedIdsToTheFile()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>(Researcher.ToString());
        var content = PackageSamples.Researcher("Research");
        var withAccount = content with { Manifest = content.Manifest with
        {
            Accounts = [new PackageAccount("twitter", "twitter", "Account to watch")]
        } };
        var revision = await package.Commit(brain.Commit(null, withAccount, "Add account slot"));
        await package.Publish(new(Guid.NewGuid(), revision.Id));
        Caller.Clear();
        var app = brain.Get<IApp>(Key());
        var reference = new PackageRevisionRef(Researcher, revision.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => app.Install(new(Guid.NewGuid(), reference, new Dictionary<string, string>())));
        var installed = await app.Install(new(Guid.NewGuid(), reference, new Dictionary<string, string>(),
            new Dictionary<string, string> { ["twitter"] = "bob-twitter" }));

        Assert.Equal("bob-twitter", installed.Accounts!["twitter"]);
        Assert.Equal("bob-twitter", File(installed).Settings["Account__twitter"]);
        Assert.DoesNotContain("Credential", File(installed).Settings.Keys);
        var changed = await app.Configure(new(Guid.NewGuid(), new Dictionary<string, string>(),
            new Dictionary<string, string> { ["twitter"] = "other-twitter" }));
        Assert.Equal("other-twitter", changed.Accounts!["twitter"]);
        Assert.Equal("other-twitter", File(changed).Settings["Account__twitter"]);
    }

    [Fact]
    public async Task UpgradeRetainsExistingAccountAndRequiresNewSlotsBeforeRetiringTheRunningFile()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>(Researcher.ToString());
        var source = PackageSamples.Researcher("Research");
        var first = await package.Commit(brain.Commit(null, source with { Manifest = source.Manifest with
        {
            Accounts = [new PackageAccount("twitter", "twitter", "Watch")]
        } }, "First"));
        var second = await package.Commit(brain.Commit(first.Id, source with { Manifest = source.Manifest with
        {
            Accounts = [new PackageAccount("twitter", "twitter", "Watch"), new PackageAccount("notify", "notification", "Notify")]
        } }, "Second"));
        Caller.Clear();
        var app = brain.Get<IApp>(Key());
        var installed = await app.Install(new(Guid.NewGuid(), new(Researcher, first.Id), new Dictionary<string, string>(),
            new Dictionary<string, string> { ["twitter"] = "alice-twitter" }));

        await Assert.ThrowsAsync<ArgumentException>(() => app.Upgrade(new(Guid.NewGuid(), new(Researcher, second.Id))));
        Assert.False(RecordingCSharpFile.Deleted.ContainsKey(installed.CSharpFiles.Single()));
        var upgraded = await app.Upgrade(new(Guid.NewGuid(), new(Researcher, second.Id),
            new Dictionary<string, string> { ["twitter"] = "alice-twitter", ["notify"] = "alice-ui" }));
        Assert.Equal("alice-twitter", upgraded.Accounts!["twitter"]);
        Assert.Equal("alice-ui", upgraded.Accounts["notify"]);
    }

    [Fact]
    public async Task InstallRunsTheRevisionSourceWithTheAppAddressAndChosenSettings()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var key = Key();

        var installed = await brain.Get<IApp>(key).Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string> { ["style"] = "bullets" }));

        Assert.Equal(AppStatus.Installed, installed.Status);
        Assert.Equal(new PackageRevisionRef(Researcher, revision.Id), installed.Revision);
        Assert.Equal("bullets", installed.Settings["style"]);
        Assert.Equal("research", Assert.Single(installed.Operations).Name);
        var file = File(installed);
        Assert.Equal(CSharpFileStatus.Running, file.Status);
        Assert.Equal(revision.Content.Source, file.Source);
        Assert.Equal(key, file.Settings["App"]);
        Assert.Equal("bullets", file.Settings["style"]);
    }

    [Fact]
    public async Task DefaultsFillOmittedSettingsAndUndeclaredSettingsAreRefused()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");

        var installed = await brain.Get<IApp>(Key()).Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        Assert.Equal("plain", installed.Settings["style"]);

        var refused = brain.Get<IApp>(Key());
        await Assert.ThrowsAsync<ArgumentException>(() => refused.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string> { ["tone"] = "loud" })));
        await Assert.ThrowsAsync<ArgumentException>(() => refused.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string> { ["style"] = new string('x', 4097) })));
        Assert.Equal(AppStatus.NotInstalled, (await refused.Read()).Status);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => refused.Install(new(Guid.NewGuid(), new(Researcher, "missing"), new Dictionary<string, string>())));
    }

    [Fact]
    public async Task ConfiguringRunsTheSameSourceInAFreshFileWithoutForking()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        var installed = await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        var configure = new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string> { ["style"] = "brief" });

        var configured = await app.Configure(configure);
        var repeated = await app.Configure(configure);

        Assert.Equal("brief", configured.Settings["style"]);
        Assert.Equal(configured.CSharpFiles, repeated.CSharpFiles);
        Assert.NotEqual(installed.CSharpFiles.Single(), configured.CSharpFiles.Single());
        Assert.True(RecordingCSharpFile.Deleted.ContainsKey(installed.CSharpFiles.Single()));
        Assert.Equal(revision.Content.Source, File(configured).Source);
        Assert.Equal("brief", File(configured).Settings["style"]);
    }

    [Fact]
    public async Task CommandsRetriedAfterTheirStateFailedToSaveFinishWithoutDuplicatingWork()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        var install = new InstallApp(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string> { ["style"] = "bullets" });

        brain.Storage.FailNextWrite = state => state is AppState { Status: AppStatus.Installed };
        await Assert.ThrowsAnyAsync<Exception>(() => app.Install(install));
        var installed = await app.Install(install);
        Assert.Equal(CSharpFileStatus.Running, File(installed).Status);

        var configure = new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string> { ["style"] = "brief" });
        brain.Storage.FailNextWrite = state => state is AppState { Status: AppStatus.Installed } app && app.Settings["style"] == "brief";
        await Assert.ThrowsAnyAsync<Exception>(() => app.Configure(configure));
        var configured = await app.Configure(configure);
        Assert.Equal("brief", File(configured).Settings["style"]);
        Assert.True(RecordingCSharpFile.Deleted.ContainsKey(installed.CSharpFiles.Single()));

        var uninstall = new UninstallApp(Guid.NewGuid());
        brain.Storage.FailNextWrite = state => state is AppState { Status: AppStatus.Uninstalled };
        await Assert.ThrowsAnyAsync<Exception>(() => app.Uninstall(uninstall));
        Assert.Equal(AppStatus.Uninstalled, (await app.Uninstall(uninstall)).Status);
        Assert.True(RecordingCSharpFile.Deleted.ContainsKey(configured.CSharpFiles.Single()));
    }

    [Fact]
    public async Task ARetryListingTheSameSettingsInAnotherOrderIsTheSameCommand()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        var operation = Guid.NewGuid();

        await app.Configure(new(operation, new Dictionary<string, string> { ["style"] = "brief", ["language"] = "uk" }));
        var retried = await app.Configure(new(operation, new Dictionary<string, string> { ["language"] = "uk", ["style"] = "brief" }));

        Assert.Equal(("brief", "uk"), (retried.Settings["style"], retried.Settings["language"]));
    }

    [Fact]
    public async Task UpgradingStaysWithinThePackageAndKeepsChosenSettings()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var first = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        await app.Install(new(Guid.NewGuid(), new(Researcher, first.Id), new Dictionary<string, string> { ["style"] = "bullets" }));
        var second = await Publish(brain, "Investigate");

        var upgraded = await app.Upgrade(new(Guid.NewGuid(), new(Researcher, second.Id)));

        Assert.Equal(second.Id, upgraded.Revision!.Revision);
        Assert.Equal("bullets", upgraded.Settings["style"]);
        Assert.Equal(second.Content.Source, File(upgraded).Source);
        Caller.As("bob");
        await brain.Get<IPackage>("bob/researcher").Fork(new(Guid.NewGuid(), new(Researcher, second.Id)));
        await Assert.ThrowsAsync<ArgumentException>(() => app.Upgrade(new(Guid.NewGuid(), new(PackageId.Parse("bob/researcher"), second.Id))));
    }

    [Fact]
    public async Task AnInvocationIsSignalledToTheScriptAndCompletedByItsResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await PackageBrain.StartAsync(ct);
        var revision = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        await using var invoked = await brain.Brain.Observe<AppInvoked>(app, ct);
        var invoke = new InvokeApp(Guid.NewGuid(), "research", "What is Orleans?");

        var pending = await app.Invoke(invoke);
        var signal = await invoked.NextAsync(ct: ct);

        Assert.Equal(InvocationStatus.Pending, pending.Status);
        Assert.Equal((invoke.InvocationId, "research", "What is Orleans?"), (signal.InvocationId, signal.Operation, signal.Input));
        Assert.Equal(invoke.InvocationId, Assert.Single(await app.Pending()).Id);
        Assert.Equal(pending.RequestedAt, (await app.Invoke(invoke)).RequestedAt);

        var completed = await app.Respond(new(invoke.InvocationId, "A virtual actor framework.", null));
        await app.Respond(new(invoke.InvocationId, "A late duplicate.", null));

        Assert.Equal(InvocationStatus.Completed, completed.Status);
        Assert.Equal("A virtual actor framework.", (await app.ReadInvocation(invoke.InvocationId)).Output);
        Assert.Empty(await app.Pending());
        await Assert.ThrowsAsync<ArgumentException>(() => app.Invoke(new(Guid.NewGuid(), "unknown", "input")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => app.ReadInvocation(Guid.NewGuid()));
    }

    [Fact]
    public async Task UninstallingStopsTheScriptAndAReinstallGetsAFreshFile()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        var installed = await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        var waiting = await app.Invoke(new(Guid.NewGuid(), "research", "Unanswered"));

        var uninstalled = await app.Uninstall(new(Guid.NewGuid()));

        Assert.Equal(AppStatus.Uninstalled, uninstalled.Status);
        Assert.True(RecordingCSharpFile.Deleted.ContainsKey(installed.CSharpFiles.Single()));
        Assert.Equal(InvocationStatus.Failed, (await app.ReadInvocation(waiting.Id)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Invoke(new(Guid.NewGuid(), "research", "After uninstall")));

        var reinstalled = await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        Assert.NotEqual(installed.CSharpFiles.Single(), reinstalled.CSharpFiles.Single());
        Assert.Equal(CSharpFileStatus.Running, File(reinstalled).Status);
    }

    [Fact]
    public async Task ARevisionWithSeveralBehaviorsRunsOneFilePerBehavior()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/tracker");
        var revision = await package.Commit(brain.Commit(null, PackageSamples.Tracker(), "Two behaviors"));
        await package.Publish(new(Guid.NewGuid(), revision.Id));
        Caller.Clear();
        var key = Key();

        var installed = await brain.Get<IApp>(key).Install(new(Guid.NewGuid(), new(PackageId.Parse("alice/tracker"), revision.Id), new Dictionary<string, string>()));

        Assert.Equal(2, installed.CSharpFiles.Count);
        foreach (var file in installed.CSharpFiles.Select(id => RecordingCSharpFile.Files[id]))
        {
            Assert.Equal(CSharpFileStatus.Running, file.Status);
            Assert.Equal(key, file.Settings["App"]);
        }
        var sources = installed.CSharpFiles.Select(id => RecordingCSharpFile.Files[id].Source).Order().ToArray();
        Assert.Equal(["// renders the report", "// watches the feed"], sources);
    }

    [Fact]
    public async Task AWhitespaceOrOversizedBehaviorIsRefusedAtCommit()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/tracker");
        var content = PackageSamples.Tracker();

        var whitespace = content with { Files = new Dictionary<string, string>(content.Files!) { [PackageContent.BehaviorsPrefix + "empty.cs"] = "   \n" } };
        await Assert.ThrowsAsync<ArgumentException>(() => package.Commit(brain.Commit(null, whitespace, "Empty behavior")));

        var oversized = content with { Files = new Dictionary<string, string>(content.Files!) { [PackageContent.BehaviorsPrefix + "big.cs"] = new string('x', 128 * 1024 + 1) } };
        await Assert.ThrowsAsync<ArgumentException>(() => package.Commit(brain.Commit(null, oversized, "Oversized behavior")));
    }

    [Fact]
    public async Task AllBehaviorsRetireOnUninstall()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        Caller.As("alice");
        var package = brain.Get<IPackage>("alice/tracker");
        var revision = await package.Commit(brain.Commit(null, PackageSamples.Tracker(), "Two behaviors"));
        await package.Publish(new(Guid.NewGuid(), revision.Id));
        Caller.Clear();
        var app = brain.Get<IApp>(Key());
        var installed = await app.Install(new(Guid.NewGuid(), new(PackageId.Parse("alice/tracker"), revision.Id), new Dictionary<string, string>()));

        await app.Uninstall(new(Guid.NewGuid()));

        Assert.Equal(2, installed.CSharpFiles.Count);
        Assert.All(installed.CSharpFiles, id => Assert.True(RecordingCSharpFile.Deleted.ContainsKey(id)));
    }

    private static async Task<PackageRevision> Publish(PackageBrain brain, string verb)
    {
        Caller.As("alice");
        var package = brain.Get<IPackage>(Researcher.ToString());
        var revision = await package.Commit(brain.Commit((await package.Read()).Head, PackageSamples.Researcher(verb), verb));
        await package.Publish(new(Guid.NewGuid(), revision.Id));
        Caller.Clear();
        return revision;
    }

    private static string Key() => "workspace-test/apps/" + Guid.NewGuid().ToString("N");

    private static CSharpFileSnapshot File(AppSnapshot app) => RecordingCSharpFile.Files[app.CSharpFiles.Single()];
}
