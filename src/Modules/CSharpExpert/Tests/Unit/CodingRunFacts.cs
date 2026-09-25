using DigitalBrain.Contracts;
using DigitalBrain.CSharpExpert;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodingRunFacts
{
    private const string Plan = """
        {
          "summary": "Add an unread count",
          "steps": [
            { "number": 1, "title": "Count unread", "files": ["Inbox.cs"], "detail": "Expose an unread count on Inbox." },
            { "number": 2, "title": "Cover it", "files": ["InboxTests.cs"], "detail": "Assert the unread count." }
          ],
          "openQuestions": []
        }
        """;

    [Fact]
    public async Task RequestFlowsThroughUnderstandAndPlanToADraftedPlan()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = new ScriptedAgentScript { Reply = Plan };
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, script);
        var run = brain.Get<ICodingRun>("pipeline");
        await using var drafted = await brain.Observe<PlanDrafted>(run, ct);
        await using var behavior = brain.RunBehavior((live, token) => RunPipeline(live, "pipeline", token), ct);
        await behavior.WaitForSubscriptionAsync<FeatureRequested>(run, ct);
        await behavior.WaitForSubscriptionAsync<ContextReady>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Expose an unread count."));
        var signal = await drafted.NextAsync(ct: ct);
        Assert.Equal("Add an unread count", signal.Plan.Summary);
        Assert.Equal(2, signal.Plan.Steps.Count);
        Assert.Contains("Expose an unread count.", script.LastPrompt);
        Assert.Contains("net11.0", script.LastPrompt);
        Assert.Equal(CodingRunStatus.PlanDrafted, (await run.Read()).Status);
    }

    [Fact]
    public async Task AMalformedPlannerReplyFailsTheRunWithAReason()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = new ScriptedAgentScript { Reply = "this is not a plan" };
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, script);
        var run = brain.Get<ICodingRun>("malformed");
        await using var failed = await brain.Observe<RunFailed>(run, ct);
        await using var behavior = brain.RunBehavior((live, token) => new PlanFeature(live, "malformed").RunAsync(token), ct);
        await behavior.WaitForSubscriptionAsync<ContextReady>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Expose an unread count."));
        await run.RecordContext(new ProjectModel(CSharpExpertTestHost.SampleSolution, [], [], 0, 0, string.Empty));
        var signal = await failed.NextAsync(ct: ct);
        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.Failed, snapshot.Status);
        Assert.Equal(signal.Reason, snapshot.FailureReason);
        Assert.Contains("valid JSON", signal.Reason);
    }

    [Fact]
    public async Task ClarifyRedraftsThePlanWithEveryClarificationIncluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = new ScriptedAgentScript { Reply = Plan };
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, script);
        var run = brain.Get<ICodingRun>("clarify");
        await using var drafted = await brain.Observe<PlanDrafted>(run, ct);
        await using var behavior = brain.RunBehavior((live, token) => RunPipeline(live, "clarify", token), ct);
        await behavior.WaitForSubscriptionAsync<FeatureRequested>(run, ct);
        await behavior.WaitForSubscriptionAsync<ContextReady>(run, ct);
        await behavior.WaitForSubscriptionAsync<PlanClarified>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Expose an unread count."));
        await drafted.NextAsync(ct: ct);
        await run.Clarify("Include the dismissed count too.");
        var redraft = await drafted.NextAsync(ct: ct);
        Assert.Contains("Include the dismissed count too.", script.LastPrompt);
        Assert.Equal(2, redraft.Plan.Steps.Count);
        var snapshot = await run.Read();
        Assert.Equal(CodingRunStatus.PlanDrafted, snapshot.Status);
        Assert.Contains("Include the dismissed count too.", snapshot.Clarifications);
    }

    [Fact]
    public async Task ApproveWithoutAPlanIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, new ScriptedAgentScript());
        var run = brain.Get<ICodingRun>("approve-guard");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(run.Approve);
        Assert.Contains("plan", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CodingRunStatus.Requested, (await run.Read()).Status);
    }

    [Fact]
    public async Task ApproveRaisesPlanApprovedWithTheDraftedPlan()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = new ScriptedAgentScript { Reply = Plan };
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, script);
        var run = brain.Get<ICodingRun>("approve");
        await using var drafted = await brain.Observe<PlanDrafted>(run, ct);
        await using var approved = await brain.Observe<PlanApproved>(run, ct);
        await using var behavior = brain.RunBehavior((live, token) => RunPipeline(live, "approve", token), ct);
        await behavior.WaitForSubscriptionAsync<FeatureRequested>(run, ct);
        await behavior.WaitForSubscriptionAsync<ContextReady>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Expose an unread count."));
        var plan = (await drafted.NextAsync(ct: ct)).Plan;
        await run.Approve();
        var signal = await approved.NextAsync(ct: ct);
        Assert.Equal(plan.Summary, signal.Plan.Summary);
        Assert.Equal(CodingRunStatus.PlanApproved, (await run.Read()).Status);
    }

    [Fact]
    public async Task StopRaisesRunStoppedAndBlocksEveryFurtherAction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, new ScriptedAgentScript());
        var run = brain.Get<ICodingRun>("stop");
        await using var stopped = await brain.Observe<RunStopped>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Expose an unread count."));
        await run.Stop();
        await stopped.NextAsync(ct: ct);
        Assert.Equal(CodingRunStatus.Stopped, (await run.Read()).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Again")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Clarify("Add the dismissed count."));
        await Assert.ThrowsAsync<InvalidOperationException>(run.Approve);
    }

    [Fact]
    public async Task APanickingPlannerFailsTheRunAndTheNextRequestStillPlans()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = new ScriptedAgentScript { Reply = Plan, Throw = new InvalidOperationException("planner offline") };
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, script);
        var run = brain.Get<ICodingRun>("planner-failure");
        await using var failed = await brain.Observe<RunFailed>(run, ct);
        await using var drafted = await brain.Observe<PlanDrafted>(run, ct);
        await using var behavior = brain.RunBehavior((live, token) => new PlanFeature(live, "planner-failure").RunAsync(token), ct);
        await behavior.WaitForSubscriptionAsync<ContextReady>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Expose an unread count."));
        await run.RecordContext(new ProjectModel(CSharpExpertTestHost.SampleSolution, [], [], 0, 0, string.Empty));
        var failure = await failed.NextAsync(ct: ct);
        Assert.Contains("planner offline", failure.Reason);
        Assert.Equal(CodingRunStatus.Failed, (await run.Read()).Status);

        script.Throw = null;
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Try again."));
        await run.RecordContext(new ProjectModel(CSharpExpertTestHost.SampleSolution, [], [], 0, 0, string.Empty));
        var plan = await drafted.NextAsync(ct: ct);
        Assert.Equal("Add an unread count", plan.Plan.Summary);
        Assert.Equal(CodingRunStatus.PlanDrafted, (await run.Read()).Status);
    }

    private static Task RunPipeline(IDigitalBrain live, string runId, CancellationToken ct)
        => Task.WhenAll(new UnderstandSolution(live, runId).RunAsync(ct), new PlanFeature(live, runId).RunAsync(ct));
}
