using System.Diagnostics;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Specs.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Specs;

[GrainType("specs.feature")]
internal sealed class FeatureNeuron(
    [PersistentState("specs.feature", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<FeatureSnapshot> store,
    TimeProvider clock)
    : Neuron<FeatureSnapshot>(store), IFeature
{
    private const int MaxSpecLength = 64 * 1024;

    private string FeatureId => this.GetPrimaryKeyString();
    private StepBinder Binder => new(ServiceProvider.GetServices<StepLibrary>());

    public async Task<FeatureSnapshot> Set(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxSpecLength) { throw new ArgumentException("A spec is at most 64 KiB.", nameof(text)); }
        if (text == Snapshot.Text && Snapshot.Revision > 0) { return Snapshot; }
        var next = Bind(text) with { Revision = Snapshot.Revision + 1 };
        await Save(next, new FeatureChanged(FeatureId, next.Revision, next.FullyBound));
        return Snapshot;
    }

    public async Task<FeatureRun> Run(string subject, IReadOnlyList<string>? skipTags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var feature = Snapshot;
        if (feature.Revision == 0) { throw new InvalidOperationException("Set the spec before running it."); }
        if (feature.Problem is { } problem) { throw new InvalidOperationException($"Line {problem.Line}: {problem.Message}"); }
        // Libraries may have changed since Set, so bind again against what the brain understands now.
        feature = Bind(feature.Text) with { Revision = feature.Revision };
        var binder = Binder;
        var startedAt = clock.GetUtcNow();
        var results = new List<ScenarioResult>();
        foreach (var scenario in feature.Scenarios)
        {
            var result = scenario.Tags.Any(tag => skipTags?.Contains(tag, StringComparer.OrdinalIgnoreCase) == true)
                ? new ScenarioResult(scenario.Name, scenario.Line, Verdict.Skipped, [])
                : await RunScenario(binder, subject, feature.Background, scenario);
            results.Add(result);
            await PublishAsync(new ScenarioFinished(FeatureId, result));
        }
        var run = new FeatureRun(feature.Revision, subject, results.ToArray(), startedAt, clock.GetUtcNow());
        await Save(feature with { LastRun = run }, new FeatureChanged(FeatureId, feature.Revision, feature.FullyBound),
            new FeatureVerified(FeatureId, feature.Revision, run.Green));
        return run;
    }

    public Task<FeatureSnapshot> Read() => Task.FromResult(Snapshot);

    public Task<IReadOnlyList<StepPattern>> Vocabulary() => Task.FromResult(Binder.Vocabulary);

    private FeatureSnapshot Bind(string text)
    {
        try
        {
            var parsed = GherkinParser.Parse(text);
            var binder = Binder;
            return new FeatureSnapshot
            {
                Text = text,
                Name = parsed.Name,
                Background = [.. parsed.Background.Select(binder.Bind)],
                Scenarios = [.. parsed.Scenarios.Select(scenario => new ScenarioOutline(scenario.Name, scenario.Line, [.. scenario.Steps.Select(binder.Bind)], scenario.Tags.ToArray()))],
            };
        }
        catch (SpecSyntaxException error)
        {
            return new FeatureSnapshot { Text = text, Problem = new(error.Line, error.Message) };
        }
    }

    private async Task<ScenarioResult> RunScenario(StepBinder binder, string subject, IReadOnlyList<BoundStep> background, ScenarioOutline scenario)
    {
        var scenarioSubject = subject.Replace(FeatureSnapshot.ScenarioPlaceholder, scenario.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var context = new StepContext(scenarioSubject, GrainFactory, ServiceProvider, CancellationToken.None);
        var steps = background.Concat(scenario.Steps).ToArray();
        var results = new List<StepResult>();
        var verdict = steps.Any(step => !step.Bound) ? Verdict.Unbound : Verdict.Passed;
        foreach (var step in steps)
        {
            if (verdict != Verdict.Passed)
            {
                results.Add(new(step.Line, step.Bound ? Verdict.Skipped : Verdict.Unbound, step.Bound ? null : "No step in the brain matches this phrasing.", TimeSpan.Zero));
                continue;
            }
            var resolved = binder.Resolve(step)!.Value;
            var watch = Stopwatch.StartNew();
            try
            {
                await resolved.Definition.Run(context, resolved.Arguments);
                results.Add(new(step.Line, Verdict.Passed, null, watch.Elapsed));
            }
            catch (Exception error)
            {
                verdict = Verdict.Failed;
                results.Add(new(step.Line, Verdict.Failed, error is StepFailedException ? error.Message : $"{error.GetType().Name}: {error.Message}", watch.Elapsed));
            }
        }
        return new(scenario.Name, scenario.Line, verdict, results.ToArray());
    }
}
