using DigitalBrain.CSharpExpert;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class RunGuardFacts
{
    private static readonly CodingPlan OneStep = new("Add Clear", [new PlanStep(1, "Add Clear", ["SampleInbox/Inbox.cs"], "Add a Clear method.")], []);

    [Fact]
    public async Task AnApprovedRunRejectsClarifyApproveAndANewRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, new ScriptedAgentScript());
        var run = brain.Get<ICodingRun>("approved");
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add Clear."));
        await run.RecordContext(new ProjectModel(CSharpExpertTestHost.SampleSolution, [], [], 0, 0, string.Empty));
        await run.RecordPlan(OneStep);
        await run.Approve();

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Clarify("Also add Count."));
        await Assert.ThrowsAsync<InvalidOperationException>(run.Approve);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.RecordPlan(OneStep));
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Again")));
        Assert.Equal(CodingRunStatus.PlanApproved, (await run.Read()).Status);
    }

    [Fact]
    public async Task StopAndFailKeepTheOutcomeOfAnEndedRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, new ScriptedAgentScript());
        var run = brain.Get<ICodingRun>("ended");
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add Clear."));
        await run.Fail("planner offline");

        await run.Stop();
        await run.Fail("second failure");

        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.Failed, snapshot.Status);
        Assert.Equal("planner offline", snapshot.FailureReason);
    }

    [Fact]
    public async Task APlanWrappedInAMarkdownFenceIsRead()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = new ScriptedAgentScript
        {
            Reply = """
                ```json
                {"summary":"Add Clear","steps":[{"number":1,"title":"Add Clear","files":["SampleInbox/Inbox.cs"],"detail":"Add it."}],"openQuestions":[]}
                ```
                """,
        };
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, script);
        var run = brain.Get<ICodingRun>("fenced");
        await using var drafted = await brain.Observe<PlanDrafted>(run, ct);
        await using var behavior = brain.RunBehavior((live, token) => new PlanFeature(live, "fenced").RunAsync(token), ct);
        await behavior.WaitForSubscriptionAsync<ContextReady>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add Clear."));
        await run.RecordContext(new ProjectModel(CSharpExpertTestHost.SampleSolution, [], [], 0, 0, string.Empty));

        var plan = await drafted.NextAsync(ct: ct);

        Assert.Equal("Add Clear", plan.Plan.Summary);
    }

    [Fact]
    public void OnlyExistingSolutionsUnderAnAllowedFolderAreAccepted()
    {
        var sampleFolder = Path.GetDirectoryName(CSharpExpertTestHost.SampleSolution)!;
        var policy = new SolutionPolicy(
            Options.Create(new CSharpExpertModuleOptions { AllowedSolutionRoots = [sampleFolder] }),
            new ConfigurationBuilder().Build());

        Assert.Equal(Path.GetFullPath(CSharpExpertTestHost.SampleSolution), policy.Validate(CSharpExpertTestHost.SampleSolution));
        Assert.Throws<InvalidOperationException>(() => policy.Validate(Path.Combine(sampleFolder, "missing.sln")));
        Assert.Throws<InvalidOperationException>(() => policy.Validate(Path.Combine(sampleFolder, "SampleInbox", "Inbox.cs")));
        Assert.Throws<InvalidOperationException>(() => policy.Validate(typeof(RunGuardFacts).Assembly.Location.Replace(".dll", ".csproj")));
    }

    [Theory]
    [InlineData(@"C:\work\run-abc\Inbox.cs", @"C:\work\run-abc", true)]
    [InlineData(@"C:\work\run-abc2\secret.cs", @"C:\work\run-abc", false)]
    [InlineData(@"C:\work\run-abc\..\other.cs", @"C:\work\run-abc", false)]
    public void ContainmentRejectsSiblingFolders(string path, string root, bool expected)
        => Assert.Equal(expected, SolutionPolicy.IsUnder(Path.GetFullPath(path), root));
}
