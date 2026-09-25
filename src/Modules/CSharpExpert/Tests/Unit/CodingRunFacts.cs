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

    private static Task RunPipeline(IDigitalBrain live, string runId, CancellationToken ct)
        => Task.WhenAll(new UnderstandSolution(live, runId).RunAsync(ct), new PlanFeature(live, runId).RunAsync(ct));
}
