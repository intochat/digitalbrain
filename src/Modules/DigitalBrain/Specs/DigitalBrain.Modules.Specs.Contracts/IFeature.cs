using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Specs;

// One .feature spec. Its steps bind to platform-owned step libraries only, so a green run means the
// behavior the text describes, not whatever its author chose to check.
[Alias("specs.feature"), Orleans.Metadata.DefaultGrainType("specs.feature")]
public interface IFeature : INeuron
{
    Task<FeatureSnapshot> Set(string text);
    // The subject is whatever the step libraries act on, for example the key of an app under test.
    // "{scenario}" in it is replaced by each scenario's line, so every scenario can get its own subject.
    // Scenarios tagged with any of skipTags are reported as skipped, so such a run is never green.
    [ResponseTimeout("01:00:00")] Task<FeatureRun> Run(string subject, IReadOnlyList<string>? skipTags = null);
    [ReadOnly, AlwaysInterleave] Task<FeatureSnapshot> Read();
    // Every step phrasing the brain understands, for authors and for highlighting.
    [ReadOnly, AlwaysInterleave] Task<IReadOnlyList<StepPattern>> Vocabulary();
}

[GenerateSerializer, Alias("specs.feature-snapshot")]
public sealed record FeatureSnapshot
{
    public const string ScenarioPlaceholder = "{scenario}";

    [Id(0)] public long Revision { get; init; }
    [Id(1)] public string Text { get; init; } = "";
    [Id(2)] public string Name { get; init; } = "";
    [Id(3)] public ScenarioOutline[] Scenarios { get; init; } = [];
    [Id(4)] public BoundStep[] Background { get; init; } = [];
    [Id(5)] public SpecProblem? Problem { get; init; }
    [Id(6)] public FeatureRun? LastRun { get; init; }
    public bool FullyBound => Problem is null && Background.Concat(Scenarios.SelectMany(scenario => scenario.Steps)).All(step => step.Bound);
}

[GenerateSerializer, Alias("specs.scenario-outline")]
public sealed record ScenarioOutline(
    [property: Id(0)] string Name,
    [property: Id(1)] int Line,
    [property: Id(2)] BoundStep[] Steps,
    [property: Id(3)] string[] Tags);

// Parameters are character ranges inside Text, so a client can highlight them in place.
[GenerateSerializer, Alias("specs.bound-step")]
public sealed record BoundStep(
    [property: Id(0)] int Line,
    [property: Id(1)] string Keyword,
    [property: Id(2)] string Text,
    [property: Id(3)] string? Pattern,
    [property: Id(4)] StepParameter[] Parameters,
    [property: Id(5)] string? DocString = null)
{
    public bool Bound => Pattern is not null;
}

[GenerateSerializer, Alias("specs.step-parameter")]
public sealed record StepParameter([property: Id(0)] int Start, [property: Id(1)] int Length, [property: Id(2)] string Kind);

[GenerateSerializer, Alias("specs.step-pattern")]
public sealed record StepPattern([property: Id(0)] string Pattern, [property: Id(1)] string Library, [property: Id(2)] string Description);

[GenerateSerializer, Alias("specs.problem")]
public sealed record SpecProblem([property: Id(0)] int Line, [property: Id(1)] string Message);

public enum Verdict { Passed, Failed, Unbound, Skipped }

[GenerateSerializer, Alias("specs.feature-run")]
public sealed record FeatureRun(
    [property: Id(0)] long FeatureRevision,
    [property: Id(1)] string Subject,
    [property: Id(2)] ScenarioResult[] Scenarios,
    [property: Id(3)] DateTimeOffset StartedAt,
    [property: Id(4)] DateTimeOffset CompletedAt)
{
    public bool Green => Scenarios.Length > 0 && Scenarios.All(scenario => scenario.Verdict == Verdict.Passed);
}

[GenerateSerializer, Alias("specs.scenario-result")]
public sealed record ScenarioResult(
    [property: Id(0)] string Name,
    [property: Id(1)] int Line,
    [property: Id(2)] Verdict Verdict,
    [property: Id(3)] StepResult[] Steps);

[GenerateSerializer, Alias("specs.step-result")]
public sealed record StepResult(
    [property: Id(0)] int Line,
    [property: Id(1)] Verdict Verdict,
    [property: Id(2)] string? Message,
    [property: Id(3)] TimeSpan Elapsed);
