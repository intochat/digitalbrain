using DigitalBrain.CSharpExpert;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class UnderstandSolutionFacts
{
    [Fact]
    public async Task UnderstandProducesAProjectModelForTheSampleSolution()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await CSharpExpertTestHost.StartAsync(ct, new ScriptedAgentScript());
        var run = brain.Get<ICodingRun>("understand");
        await using var context = await brain.Observe<ContextReady>(run, ct);
        await using var behavior = brain.RunBehavior((live, token) => new UnderstandSolution(live, "understand").RunAsync(token), ct);
        await behavior.WaitForSubscriptionAsync<FeatureRequested>(run, ct);
        await run.Request(new FeatureRequest(CSharpExpertTestHost.SampleSolution, "Expose an unread count."));
        var model = (await context.NextAsync(ct: ct)).Model;
        Assert.Equal(CSharpExpertTestHost.SampleSolution, model.SolutionPath);
        Assert.Equal(2, model.Projects.Count);
        Assert.Contains(model.Projects, project => project.Name == "SampleInbox");
        Assert.Contains(model.Projects, project => project.Name == "SampleInbox.Tests");
        Assert.All(model.Projects, project => Assert.Equal(1, project.DocumentCount));
        Assert.Contains("net11.0", model.TargetFrameworks);
        Assert.Contains("SampleInbox", model.MapText);
        Assert.Contains("SampleInbox.Tests -> SampleInbox", model.MapText);
        Assert.Equal(0, model.ErrorCount);
    }
}
