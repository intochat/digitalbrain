using DigitalBrain.SmartPrompt;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

public sealed class BehaviorExamplesTests
{
    [Fact]
    public void All_nine_examples_compile_and_cover_supported_event_sources()
    {
        var compiler = BehaviorCompiler.CreateDefault();

        Assert.Equal(9, BehaviorExamples.All.Count);
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
            Assert.NotEmpty(plan.Behaviors[0].TriggerKey);
            triggerKeys.Add(plan.Behaviors[0].TriggerKey);
        }
    }

    [Fact]
    public void X_example_preserves_the_source_link_and_configured_reasoning_contract()
    {
        var example = Assert.Single(BehaviorExamples.All, static candidate => candidate.Name == "bitcoin-tracker");
        var behavior = Assert.Single(BehaviorCompiler.CreateDefault().Compile(example.Source).Plan!.Behaviors);

        Assert.Contains(behavior.Steps, static step => step.Binding == "AnalyzeWithConfiguredLlm");
        Assert.Contains(behavior.Steps, static step => step.Binding == "AddChartPoint");
        Assert.Equal("x.post/account:elonmusk", behavior.TriggerKey);
    }

    [Fact]
    public void Salesforce_enrichment_experience_starts_with_a_learnable_baseline()
    {
        var example = Assert.Single(BehaviorExamples.All,
            static candidate => candidate.Name == "salesforce-account-enrichment");
        var plan = BehaviorCompiler.CreateDefault().Compile(example.Source).Plan;
        Assert.NotNull(plan);
        var behavior = Assert.Single(plan.Behaviors);
        Assert.DoesNotContain(behavior.Steps,
            static step => step.Binding == "PreserveVerifiedSalesforceFields");
        Assert.Contains(Assert.Single(plan.Tests).Steps,
            static step => step.Binding == "AssertChatNotification");
    }
}
