using DigitalBrain.Apps;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Verification;

// Verification runs a revision's tests.cs as a sandbox script; the dbtest lines it prints and its
// exit code are the whole verdict, and a revision with tests reaches the marketplace only when green.
public sealed class AppVerificationFacts
{
    private static readonly PackageId Echo = PackageId.Parse("alice/echo");

    [Fact]
    public async Task AConfigurationAppAnswersThroughItsRuntimeWithoutAScript()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, tests: null);
        var app = brain.Get<IApp>("workspace-alice/apps/echo");

        var installed = await app.Install(new(Guid.NewGuid(), new(Echo, revision.Id), new Dictionary<string, string>()));
        var invocation = await app.Invoke(new(Guid.NewGuid(), "ask", "hi"));
        while (invocation.Status == InvocationStatus.Pending) { await Task.Delay(50, ct); invocation = await app.ReadInvocation(invocation.Id); }

        Assert.Empty(installed.CSharpFiles);
        Assert.Equal("you said: hi (polite)", invocation.Output);
    }

    [Fact]
    public async Task ARevisionWithTestsIsPublishedOnlyAfterTheyPass()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, tests: "// drives the scratch app");
        var package = brain.Get<IPackage>(Echo.ToString());
        RecordingCSharpFile.ScriptedRuns[$"specs/{Echo}@{revision.Id}"] = (0, "noise\ndbtest:pass It repeats what I say\nmore noise");
        Caller.As("alice");

        await Assert.ThrowsAsync<InvalidOperationException>(() => package.Publish(new(Guid.NewGuid(), revision.Id)));
        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();
        Caller.As("alice");
        var published = await package.Publish(new(Guid.NewGuid(), revision.Id));

        Assert.True(verification.Green);
        Assert.Equal(revision.Id, published.Published);
        var verdict = Assert.Single(verification.Run.Scenarios);
        Assert.Equal(("It repeats what I say", true), (verdict.Name, verdict.Passed));
    }

    [Fact]
    public async Task AFailingScenarioKeepsTheRevisionOutOfTheMarketplace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, tests: "// drives the scratch app");
        RecordingCSharpFile.ScriptedRuns[$"specs/{Echo}@{revision.Id}"] =
            (1, "dbtest:pass It greets\ndbtest:fail It repeats\tThe answer was \"you said: hi\".");

        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();

        Assert.False(verification.Green);
        var failed = verification.Run.Scenarios.Single(scenario => !scenario.Passed);
        Assert.Equal("It repeats", failed.Name);
        Assert.Contains("you said: hi", failed.Message, StringComparison.Ordinal);
        Caller.As("alice");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => brain.Get<IPackage>(Echo.ToString()).Publish(new(Guid.NewGuid(), revision.Id)));
    }

    [Fact]
    public async Task ANoScenarioRunIsNotGreen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, tests: "// exits without reporting anything");
        RecordingCSharpFile.ScriptedRuns[$"specs/{Echo}@{revision.Id}"] = (0, "the script crashed before its first scenario");

        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();

        Assert.False(verification.Green);
        Assert.Empty(verification.Run.Scenarios);
    }

    [Fact]
    public async Task VerificationHandsTheRevisionToTheTestsRunAndCleansUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, tests: "// hands the revision over");
        RecordingCSharpFile.ScriptedRuns[$"specs/{Echo}@{revision.Id}"] = (0, "dbtest:pass All good");

        await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();

        var run = RecordingCSharpFile.ConfiguredSettings.Single(pair => pair.Key.StartsWith($"specs/{Echo}@{revision.Id}", StringComparison.Ordinal));
        Assert.Equal(Echo.ToString(), run.Value["Package"]);
        Assert.Equal(revision.Id, run.Value["Revision"]);
        Assert.True(RecordingCSharpFile.Deleted.ContainsKey(run.Key));
    }

    [Fact]
    public async Task ARevisionWithoutTestsHasNothingToVerify()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, tests: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify());
    }

    private static async Task<PackageRevision> Commit(PackageBrain brain, string? tests)
    {
        Caller.As("alice");
        var files = new Dictionary<string, string> { [PackageContent.SpecPath] = "## Scenario: It repeats what I say" };
        if (tests is not null) { files[PackageContent.TestsPath] = tests; }
        var content = new PackageContent(
            new PackageManifest("Echo", "Repeats what it is told.", [new PackageOperation("ask", "Say something.")],
                [new PackageSetting("tone", "How it answers.", "polite")], Runtime: "echo"),
            "",
            tests is null ? new Dictionary<string, string>() : files);
        return await brain.Get<IPackage>(Echo.ToString()).Commit(brain.Commit(null, content));
    }

    private static Task<PackageBrain> StartAsync(CancellationToken ct)
        => PackageBrain.StartAsync(ct, silo => silo.Services.AddAppRuntime<EchoRuntime>());

    private sealed class EchoRuntime : IAppRuntime
    {
        public string Name => "echo";

        public Task<string> Answer(AppRuntimeRequest request, CancellationToken cancellationToken)
            => Task.FromResult($"you said: {request.Input} ({request.Settings["tone"]})");
    }
}
