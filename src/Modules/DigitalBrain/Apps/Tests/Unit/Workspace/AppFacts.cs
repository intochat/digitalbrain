using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Behavior;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Workspace;

public sealed class AppFacts
{
    private static readonly PackageId Researcher = PackageId.Parse("alice/researcher");

    [Fact]
    public async Task InstallRunsTheRevisionArtifactWithTheAppAddressAndChosenSettings()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var key = Key();

        var installed = await brain.Get<IApp>(key).Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string> { ["style"] = "bullets" }));

        Assert.Equal(AppStatus.Installed, installed.Status);
        Assert.Equal(new PackageRevisionRef(Researcher, revision.Id), installed.Revision);
        Assert.Equal("bullets", installed.Settings["style"]);
        Assert.Equal("research", Assert.Single(installed.Operations).Name);
        var deployment = Assert.Single(Program(installed).Deployments);
        Assert.Equal(revision.Artifact, deployment.Artifact);
        var configuration = Configuration(deployment);
        Assert.Equal(key, configuration["Behavior__App"]);
        Assert.Equal("bullets", configuration["Behavior__Settings__style"]);
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
    public async Task ConfiguringRedeploysTheSameArtifactWithoutForking()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        var configure = new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string> { ["style"] = "brief" });

        var configured = await app.Configure(configure);
        await app.Configure(configure);

        Assert.Equal("brief", configured.Settings["style"]);
        var deployments = Program(configured).Deployments;
        Assert.Equal(2, deployments.Count);
        Assert.All(deployments, deployment => Assert.Equal(revision.Artifact, deployment.Artifact));
        Assert.Equal("brief", Configuration(deployments[^1])["Behavior__Settings__style"]);
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
        Assert.Equal(second.Artifact, Program(upgraded).Deployments[^1].Artifact);
        Caller.As("bob");
        await brain.Get<IPackage>("bob/researcher").Fork(new(Guid.NewGuid(), new(Researcher, second.Id)));
        await Assert.ThrowsAsync<ArgumentException>(() => app.Upgrade(new(Guid.NewGuid(), new(PackageId.Parse("bob/researcher"), second.Id))));
    }

    [Fact]
    public async Task AnInvocationIsSignalledToTheBehaviorAndCompletedByItsResponse()
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
    public async Task UninstallingStopsTheBehaviorAndAReinstallGetsAFreshProgram()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var revision = await Publish(brain, "Research");
        var app = brain.Get<IApp>(Key());
        var installed = await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        var waiting = await app.Invoke(new(Guid.NewGuid(), "research", "Unanswered"));

        var uninstalled = await app.Uninstall(new(Guid.NewGuid()));

        Assert.Equal(AppStatus.Uninstalled, uninstalled.Status);
        Assert.True(RecordingBehaviorProgram.Deleted.ContainsKey(installed.BehaviorProgram!));
        Assert.Equal(InvocationStatus.Failed, (await app.ReadInvocation(waiting.Id)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Invoke(new(Guid.NewGuid(), "research", "After uninstall")));

        var reinstalled = await app.Install(new(Guid.NewGuid(), new(Researcher, revision.Id), new Dictionary<string, string>()));
        Assert.NotEqual(installed.BehaviorProgram, reinstalled.BehaviorProgram);
        Assert.Single(Program(reinstalled).Deployments);
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

    private static BehaviorSnapshot Program(AppSnapshot app) => RecordingBehaviorProgram.Programs[app.BehaviorProgram!];

    private static Dictionary<string, string> Configuration(BehaviorDeployment deployment)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(deployment.ConfigurationJson)!;
}
