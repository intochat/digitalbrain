using DigitalBrain.Apps;
using DigitalBrain.Specs;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Verification;

public sealed class AppVerificationFacts
{
    private static readonly PackageId Echo = PackageId.Parse("alice/echo");

    [Fact]
    public async Task AConfigurationAppAnswersThroughItsRuntimeWithoutAScript()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, Spec("the answer is \"you said: hi\""));
        var app = brain.Get<IApp>("workspace-alice/apps/echo");

        var installed = await app.Install(new(Guid.NewGuid(), new(Echo, revision.Id), new Dictionary<string, string>()));
        var invocation = await app.Invoke(new(Guid.NewGuid(), "ask", "hi"));
        while (invocation.Status == InvocationStatus.Pending) { await Task.Delay(50, ct); invocation = await app.ReadInvocation(invocation.Id); }

        Assert.Empty(installed.CSharpFiles);
        Assert.Equal("you said: hi (polite)", invocation.Output);
    }

    [Fact]
    public async Task ARevisionWithScenariosIsPublishedOnlyAfterTheyPass()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, Spec("the answer is \"you said: hi (polite)\""));
        var package = brain.Get<IPackage>(Echo.ToString());
        Caller.As("alice");

        await Assert.ThrowsAsync<InvalidOperationException>(() => package.Publish(new(Guid.NewGuid(), revision.Id)));
        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();
        Caller.As("alice");
        var published = await package.Publish(new(Guid.NewGuid(), revision.Id));

        Assert.True(verification.Green);
        Assert.Equal(revision.Id, published.Published);
        var feature = await brain.Get<IFeature>(IAppVerification.FeatureKey(new(Echo, revision.Id))).Read();
        Assert.True(feature.LastRun?.Green);
    }

    [Fact]
    public async Task AFailingScenarioKeepsTheRevisionOutOfTheMarketplace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, Spec("the answer mentions \"goodbye\""));

        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();

        Assert.False(verification.Green);
        var failed = Assert.Single(verification.Run.Scenarios).Steps.Single(step => step.Verdict == Verdict.Failed);
        Assert.Contains("does not mention \"goodbye\"", failed.Message);
        Caller.As("alice");
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IPackage>(Echo.ToString()).Publish(new(Guid.NewGuid(), revision.Id)));
    }

    [Fact]
    public async Task ScenariosCanChangeSettingsOfTheAppUnderTest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, """
            Feature: Echo
              Scenario: Tone is a setting
                Given the setting "tone" is "rude"
                When I ask "hi"
                Then the answer is "you said: hi (rude)"
            """);

        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();

        Assert.True(verification.Green, string.Join("; ", verification.Run.Scenarios.SelectMany(scenario => scenario.Steps).Select(step => step.Message)));
    }

    [Fact]
    public async Task EachScenarioStartsFromAFreshInstallation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var revision = await Commit(brain, """
            Feature: Echo
              Scenario: One scenario changes a setting
                Given the setting "tone" is "rude"
                When I ask "hi"
                Then the answer is "you said: hi (rude)"

              Scenario: The next one sees the default again
                When I ask "hi"
                Then the answer is "you said: hi (polite)"
            """);

        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(new(Echo, revision.Id))).Verify();

        Assert.True(verification.Green, string.Join("; ", verification.Run.Scenarios.SelectMany(scenario => scenario.Steps).Select(step => step.Message)));
    }

    private static string Spec(string then) => $"""
        Feature: Echo
          Scenario: It repeats what I say
            When I ask "hi"
            Then {then}
        """;

    private static async Task<PackageRevision> Commit(PackageBrain brain, string spec)
    {
        Caller.As("alice");
        var content = new PackageContent(
            new PackageManifest("Echo", "Repeats what it is told.", [new PackageOperation("ask", "Say something.")],
                [new PackageSetting("tone", "How it answers.", "polite")], Runtime: "echo"),
            "",
            new Dictionary<string, string> { [PackageContent.SpecPath] = spec });
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
