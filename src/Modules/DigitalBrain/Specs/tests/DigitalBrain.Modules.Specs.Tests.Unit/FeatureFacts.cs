using DigitalBrain.Specs;
using DigitalBrain.Specs.Signals;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Specs.Tests.Unit;

public sealed class FeatureFacts
{
    private const string Arithmetic = """
        Feature: Adding
          Background:
            Given the total is 0

          Scenario: Two additions
            When I add 2
            And I add 3
            Then the total is 5

          Scenario: A wrong expectation
            When I add 1
            Then the total is 7
            And the total is 1
        """;

    [Fact]
    public async Task StepsBindToLibraryPhrasingsWithHighlightedParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);

        var feature = await brain.Get<IFeature>("adding").Set("""
            Feature: Adding
              Scenario: Greeting
                Given the greeting is "hello there"
                Then something nobody defined
            """);

        var steps = Assert.Single(feature.Scenarios).Steps;
        Assert.Equal("the greeting is {string}", steps[0].Pattern);
        var parameter = Assert.Single(steps[0].Parameters);
        Assert.Equal("\"hello there\"", steps[0].Text.Substring(parameter.Start, parameter.Length));
        Assert.False(steps[1].Bound);
        Assert.False(feature.FullyBound);
    }

    [Fact]
    public async Task RunReportsEachScenarioAndStopsAtTheFirstFailingStep()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var feature = brain.Get<IFeature>("adding");
        await feature.Set(Arithmetic);
        await using var verified = await brain.Observe<FeatureVerified>(feature, ct);

        var run = await feature.Run("calculator");

        Assert.Equal(Verdict.Passed, run.Scenarios[0].Verdict);
        var failed = run.Scenarios[1];
        Assert.Equal(Verdict.Failed, failed.Verdict);
        Assert.Equal([Verdict.Passed, Verdict.Passed, Verdict.Failed, Verdict.Skipped], failed.Steps.Select(step => step.Verdict));
        Assert.Equal("The total is 1, not 7.", failed.Steps[2].Message);
        Assert.False(run.Green);
        Assert.False((await verified.NextAsync(ct: ct)).Green);
        Assert.Equal(run.CompletedAt, (await feature.Read()).LastRun?.CompletedAt);
    }

    [Fact]
    public async Task UnboundScenariosFailWithoutRunningAnyStep()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var feature = brain.Get<IFeature>("unbound");
        await feature.Set("Feature: f\nScenario: s\n  Given the total is 0\n  Then the moon is cheese");

        var scenario = Assert.Single((await feature.Run("calculator")).Scenarios);

        Assert.Equal(Verdict.Unbound, scenario.Verdict);
        Assert.Equal([Verdict.Skipped, Verdict.Unbound], scenario.Steps.Select(step => step.Verdict));
    }

    [Fact]
    public async Task SyntaxProblemsAreKeptWithTheTextAndBlockRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var feature = brain.Get<IFeature>("broken");

        var snapshot = await feature.Set("Feature: f\nScenario: s\n  Given the total is 0\n  oops");

        Assert.Equal(4, snapshot.Problem?.Line);
        await Assert.ThrowsAsync<InvalidOperationException>(() => feature.Run("calculator"));
    }

    [Fact]
    public async Task VocabularyListsEveryPhrasing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var vocabulary = await brain.Get<IFeature>("any").Vocabulary();
        Assert.Contains(vocabulary, pattern => pattern.Pattern == "I add {int}" && pattern.Library == nameof(ArithmeticSteps));
    }

    [Fact]
    public async Task BoundFeatureAndRunCollectionsSurviveReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var feature = brain.Get<IFeature>("durable-adding");
        await feature.Set(Arithmetic.Replace("Scenario: Two additions", "@smoke\n  Scenario: Two additions", StringComparison.Ordinal));
        await feature.Run("calculator");
        var before = await feature.Read();
        Assert.Equal("@smoke", Assert.Single(before.Scenarios[0].Tags));
        Assert.Single(Assert.Single(before.Background).Parameters);
        await brain.DeactivateAsync(feature, ct);
        var after = await feature.Read();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after));
        Assert.Equal([Verdict.Passed, Verdict.Failed], after.LastRun!.Scenarios.Select(scenario => scenario.Verdict));
    }
    private static Task<UnitBrain> StartAsync(CancellationToken ct) => UnitTest.Create().WithModule<SpecsModule>()
        .ConfigureSilo(silo => silo.Services.AddSingleton<StepLibrary, ArithmeticSteps>())
        .StartAsync(ct);

    private sealed class ArithmeticSteps : StepLibrary
    {
        public ArithmeticSteps()
        {
            Step("the total is {int}", "Sets the total, or checks it once something was added.", (context, args) =>
            {
                if (!Added(context)) { context.Remember("total", args.Int(0)); return Task.CompletedTask; }
                var total = context.Recall<int>("total");
                return total == args.Int(0) ? Task.CompletedTask : throw new StepFailedException($"The total is {total}, not {args.Int(0)}.");
            });
            Step("I add {int}", "Adds a number to the total.", (context, args) =>
            {
                context.Remember("total", context.Recall<int>("total") + args.Int(0));
                context.Remember("added", true);
                return Task.CompletedTask;
            });
            Step("the greeting is {string}", "Remembers a greeting.", (context, args) =>
            {
                context.Remember("greeting", args.Text(0));
                return Task.CompletedTask;
            });
        }

        private static bool Added(StepContext context) => context.TryRecall<bool>("added", out var added) && added;
    }
}
