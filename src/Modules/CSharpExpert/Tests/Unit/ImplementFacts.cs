using DigitalBrain.CSharpExpert;
using DigitalBrain.Microsoft.DotNet;
using DigitalBrain.Microsoft.Roslyn;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ImplementFacts
{
    private const string TwoStepPlan = """
        {
          "summary": "Add a clear operation and cover it",
          "steps": [
            { "number": 1, "title": "Add Clear", "files": ["SampleInbox/Inbox.cs"], "detail": "Add a Clear method to Inbox." },
            { "number": 2, "title": "Cover Clear", "files": ["SampleInbox.Tests/InboxTests.cs"], "detail": "Add a test for Clear." }
          ],
          "openQuestions": []
        }
        """;

    private const string OneStepPlan = """
        {
          "summary": "Add a clear operation",
          "steps": [
            { "number": 1, "title": "Add Clear", "files": ["SampleInbox/Inbox.cs"], "detail": "Add a Clear method to Inbox." }
          ],
          "openQuestions": []
        }
        """;

    private static readonly EditRequest ClearEdit = new(
        EditKind.InsertMember, SymbolId: "T:SampleInbox.Inbox", Source: "public void Clear() => _messages.Clear();");

    private static readonly EditRequest TestEdit = new(
        EditKind.InsertMember, SymbolId: "T:SampleInbox.Tests.InboxTests",
        Source: "[Fact] public void ClearRemovesMessages() { var inbox = new Inbox(); inbox.Add(\"a\"); inbox.Clear(); Assert.Empty(inbox.Messages); }");

    private static readonly EditRequest BrokenEdit = new(
        EditKind.InsertMember, SymbolId: "T:SampleInbox.Inbox", Source: "public void Broken() { Missing(); }");

    private static readonly EditRequest CountEdit = new(
        EditKind.InsertMember, SymbolId: "T:SampleInbox.Inbox", Source: "public int Count() => _messages.Count;");

    [Fact]
    public async Task AFeatureIsImplementedBuiltTestedAndFinishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = new ScriptedAgentScript { Reply = TwoStepPlan };
        agent.QueueEdits([ClearEdit]);
        agent.QueueEdits([TestEdit]);
        await using var harness = await CSharpExpertHarness.StartAsync(ct, agent);
        const string runId = "run-happy";
        var run = harness.Brain.Get<ICodingRun>(runId);
        await using var drafted = await harness.Brain.Observe<PlanDrafted>(run, ct);
        await using var finished = await harness.Brain.Observe<RunFinished>(run, ct);

        await harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add a clear operation and cover it."), ct, runId);
        await drafted.NextAsync(ct: ct);
        await run.Approve();
        await finished.NextAsync(ct: ct);

        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.Finished, snapshot.Status);
        Assert.Equal(2, snapshot.TotalSteps);
        Assert.Equal(2, snapshot.CurrentStep);
        Assert.Equal(0, snapshot.FixAttempts);
        Assert.True(snapshot.Build!.Succeeded);
        Assert.True(snapshot.Test!.Succeeded);
        Assert.Contains("Clear", await File.ReadAllTextAsync(Path.Combine(harness.WorkspaceRoot, runId, "SampleInbox", "Inbox.cs"), ct));
        Assert.Contains("ClearRemovesMessages", await File.ReadAllTextAsync(Path.Combine(harness.WorkspaceRoot, runId, "SampleInbox.Tests", "InboxTests.cs"), ct));
        Assert.DoesNotContain("Clear", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(CSharpExpertTestHost.SampleSolution)!, "SampleInbox", "Inbox.cs"), ct));
    }

    [Fact]
    public async Task ABrokenEditIsReAskedOnceAndTheRunFinishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = new ScriptedAgentScript { Reply = OneStepPlan };
        agent.QueueEdits([BrokenEdit]);
        agent.QueueEdits([ClearEdit]);
        await using var harness = await CSharpExpertHarness.StartAsync(ct, agent);
        const string runId = "run-fix";
        var run = harness.Brain.Get<ICodingRun>(runId);
        await using var drafted = await harness.Brain.Observe<PlanDrafted>(run, ct);
        await using var finished = await harness.Brain.Observe<RunFinished>(run, ct);

        await harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add a clear operation."), ct, runId);
        await drafted.NextAsync(ct: ct);
        await run.Approve();
        await finished.NextAsync(ct: ct);

        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.Finished, snapshot.Status);
        Assert.Equal(1, snapshot.FixAttempts);
        Assert.Equal(2, agent.Proposals.Count);
        Assert.Contains("CS0103", agent.Proposals[^1].Failure);
        Assert.Contains("Clear", await File.ReadAllTextAsync(Path.Combine(harness.WorkspaceRoot, runId, "SampleInbox", "Inbox.cs"), ct));
    }

    [Fact]
    public async Task AnUnfixableEditNeedsAHumanAfterMaxFixAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = new ScriptedAgentScript { Reply = OneStepPlan, DefaultEdits = [BrokenEdit] };
        await using var harness = await CSharpExpertHarness.StartAsync(ct, agent);
        const string runId = "run-needs-human";
        var run = harness.Brain.Get<ICodingRun>(runId);
        await using var drafted = await harness.Brain.Observe<PlanDrafted>(run, ct);
        await using var needsHuman = await harness.Brain.Observe<NeedsHuman>(run, ct);

        await harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add a clear operation."), ct, runId);
        await drafted.NextAsync(ct: ct);
        await run.Approve();
        var signal = await needsHuman.NextAsync(ct: ct);

        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.NeedsHuman, snapshot.Status);
        Assert.Equal(3, snapshot.FixAttempts);
        Assert.Equal(4, agent.Proposals.Count);
        Assert.Contains("CS0103", signal.Reason);
    }

    [Fact]
    public async Task AFailedBuildIsReAskedOnceAndTheRunFinishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = new ScriptedAgentScript { Reply = OneStepPlan };
        agent.QueueEdits([ClearEdit]);
        agent.QueueEdits([CountEdit]);
        var process = new ScriptedProcessScript();
        process.QueueBuild(new ProcessResult(1,
            "SampleInbox/Inbox.cs(9,20): error CS0103: The name 'Missing' does not exist [SampleInbox/SampleInbox.csproj]",
            string.Empty, TimeSpan.Zero, TimedOut: false));
        await using var harness = await CSharpExpertHarness.StartAsync(ct, agent, process);
        const string runId = "run-build-failure";
        var run = harness.Brain.Get<ICodingRun>(runId);
        await using var drafted = await harness.Brain.Observe<PlanDrafted>(run, ct);
        await using var finished = await harness.Brain.Observe<RunFinished>(run, ct);

        await harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add a clear operation."), ct, runId);
        await drafted.NextAsync(ct: ct);
        await run.Approve();
        await finished.NextAsync(ct: ct);

        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.Finished, snapshot.Status);
        Assert.Equal(1, snapshot.FixAttempts);
        Assert.True(snapshot.Build!.Succeeded);
        Assert.Equal(2, agent.Proposals.Count);
        Assert.Contains("CS0103", agent.Proposals[^1].Failure);
    }

    [Fact]
    public async Task AFailedTestIsReAskedOnceAndTheRunFinishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = new ScriptedAgentScript { Reply = OneStepPlan };
        agent.QueueEdits([ClearEdit, TestEdit]);
        agent.QueueEdits([CountEdit]);
        var process = new ScriptedProcessScript();
        process.QueueTest(new ProcessResult(1,
            "total: 1, failed: 1, succeeded: 0, skipped: 0\nfailed SampleInbox.Tests.InboxTests.ClearRemovesMessages (12ms)\n  Expected empty.\n",
            string.Empty, TimeSpan.Zero, TimedOut: false));
        await using var harness = await CSharpExpertHarness.StartAsync(ct, agent, process);
        const string runId = "run-test-failure";
        var run = harness.Brain.Get<ICodingRun>(runId);
        await using var drafted = await harness.Brain.Observe<PlanDrafted>(run, ct);
        await using var finished = await harness.Brain.Observe<RunFinished>(run, ct);

        await harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add a clear operation."), ct, runId);
        await drafted.NextAsync(ct: ct);
        await run.Approve();
        await finished.NextAsync(ct: ct);

        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.Finished, snapshot.Status);
        Assert.Equal(1, snapshot.FixAttempts);
        Assert.True(snapshot.Test!.Succeeded);
        Assert.Equal(2, agent.Proposals.Count);
        Assert.Contains("ClearRemovesMessages", agent.Proposals[^1].Failure);
    }
}
