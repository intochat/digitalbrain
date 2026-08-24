using DigitalBrain.SmartPrompt;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

public sealed class BehaviorExamplesTests
{
    [Fact]
    public void All_eight_examples_compile_and_cover_distinct_event_domains()
    {
        var compiler = BehaviorCompiler.CreateDefault();

        Assert.Equal(8, BehaviorExamples.All.Count);
        var triggerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var example in BehaviorExamples.All)
        {
            var compilation = compiler.Compile(example.Source);
            Assert.True(
                compilation.Success,
                $"{example.Name}: {string.Join(Environment.NewLine, compilation.Diagnostics.Select(static d => d.Message))}");
            var plan = Assert.IsType<BehaviorPlan>(compilation.Plan);
            Assert.Single(plan.Behaviors);
            Assert.Single(plan.Tests);
            Assert.True(triggerKeys.Add(plan.Behaviors[0].TriggerKey), example.Name);
        }
    }

    [Fact]
    public void X_example_preserves_the_source_link_and_local_reasoning_contract()
    {
        var example = Assert.Single(BehaviorExamples.All, static candidate => candidate.Name == "bitcoin-tracker");
        var behavior = Assert.Single(BehaviorCompiler.CreateDefault().Compile(example.Source).Plan!.Behaviors);

        Assert.Contains(behavior.Steps, static step => step.Binding == "AnalyzeWithGemma");
        Assert.Contains(behavior.Steps, static step => step.Binding == "AddChartPoint");
        Assert.Equal("x.post/account:elonmusk", behavior.TriggerKey);
    }
}
