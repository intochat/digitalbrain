using DigitalBrain.CSharpExpert;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodingRunHostFacts
{
    private const string OneStepPlan = """
        {
          "summary": "Add a clear operation",
          "steps": [
            { "number": 1, "title": "Add Clear", "files": ["SampleInbox/Inbox.cs"], "detail": "Add a Clear method to Inbox." }
          ],
          "openQuestions": []
        }
        """;

    [Fact]
    public async Task TheHostDropsTheSessionWhenTheRunStops()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await CSharpExpertHarness.StartAsync(ct, new ScriptedAgentScript { Reply = OneStepPlan });
        const string runId = "run-session";
        var run = harness.Brain.Get<ICodingRun>(runId);
        await using var stopped = await harness.Brain.Observe<RunStopped>(run, ct);

        await harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add a clear operation."), ct, runId);
        Assert.True(harness.Host.IsActive(runId));

        await run.Stop();
        await stopped.NextAsync(ct: ct);
        await WaitUntilAsync(() => !harness.Host.IsActive(runId), ct);
        Assert.False(harness.Host.IsActive(runId));
    }

    [Fact]
    public async Task TheHostRejectsASecondRunWithTheSameId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var harness = await CSharpExpertHarness.StartAsync(ct, new ScriptedAgentScript { Reply = OneStepPlan });
        const string runId = "run-duplicate";
        await harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Add a clear operation."), ct, runId);
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Host.StartAsync(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Again."), ct, runId));

        Assert.Equal("This run is already active.", duplicate.Message);
        Assert.Equal("Add a clear operation.", (await harness.Brain.Get<ICodingRun>(runId).Read()).Request!.Description);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(50, cancellationToken);
        }
    }
}
