using DigitalBrain.SmartPrompt;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

public sealed class BehaviorCompilerTests
{
    private const string ValidFeature =
        """
        Feature: Bitcoin tracker

          @behavior
          Scenario: Track Elon posts about Bitcoin
            Given X.Account("elonmusk")
            When a new X.Post is published
            And the post mentions "bitcoin"
            Then analyze the event as "bitcoin market signal" with Gemma
            And add UI.Chart.Point to UI.Chart("bitcoin_tracker")

          @test
          Scenario: An Elon Bitcoin post adds a linked point
            Given fake X.Post from "elonmusk" with text "Bitcoin reaches 95000" and value 95000
            When behavior "Track Elon posts about Bitcoin" runs
            Then UI.Chart("bitcoin_tracker") has point 95000 linking to the source
        """;

    [Fact]
    public void Paired_feature_compiles_to_one_reactive_scenario_and_one_test()
    {
        var compiler = BehaviorCompiler.CreateDefault();

        var compilation = compiler.Compile(ValidFeature);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(static d => d.Message)));
        var plan = Assert.IsType<BehaviorPlan>(compilation.Plan);
        var behavior = Assert.Single(plan.Behaviors);
        Assert.Equal("Track Elon posts about Bitcoin", behavior.Name);
        Assert.Equal("x.post/account:elonmusk", behavior.TriggerKey);
        Assert.Equal(5, behavior.Steps.Count);
        Assert.Single(plan.Tests);
        Assert.Equal(64, plan.SourceHash.Length);
    }

    [Fact]
    public void Unknown_step_fails_closed_with_source_line()
    {
        var source = ValidFeature.Replace(
            "And the post mentions \"bitcoin\"",
            "And perform unknowable magic",
            StringComparison.Ordinal);

        var compilation = BehaviorCompiler.CreateDefault().Compile(source);

        Assert.False(compilation.Success);
        var diagnostic = Assert.Single(compilation.Diagnostics, static d => d.Code == "BEH003");
        Assert.Equal(7, diagnostic.Line);
        Assert.Contains("perform unknowable magic", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Feature_without_a_paired_test_is_rejected()
    {
        var source = ValidFeature[..ValidFeature.IndexOf("  @test", StringComparison.Ordinal)];

        var compilation = BehaviorCompiler.CreateDefault().Compile(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, static d => d.Code == "BEH006");
    }

    [Fact]
    public void Suggestions_are_human_Gherkin_not_binding_regular_expressions()
    {
        var suggestions = BehaviorCompiler.CreateDefault().Suggestions;

        Assert.Contains(suggestions, static suggestion =>
            suggestion.Keyword == "When" && suggestion.Template == "a new X.Post is published");
        Assert.DoesNotContain(suggestions, static suggestion => suggestion.Template.Contains('\\'));
    }

    [Fact]
    public void Every_behavior_scenario_requires_exactly_one_paired_test()
    {
        var secondBehavior =
            """

          @behavior
          Scenario: Track every Elon post
            Given X.Account("elonmusk")
            When a new X.Post is published
            Then notify UI.Chat("main")

        """;
        var source = ValidFeature.Replace("  @test", secondBehavior + "  @test", StringComparison.Ordinal);

        var compilation = BehaviorCompiler.CreateDefault().Compile(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, static diagnostic =>
            diagnostic.Code == "BEH008" && diagnostic.Message.Contains("Track every Elon post", StringComparison.Ordinal));
    }
}
