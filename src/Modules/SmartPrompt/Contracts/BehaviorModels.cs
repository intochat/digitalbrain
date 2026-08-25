namespace DigitalBrain.SmartPrompt;

public enum BehaviorDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum BehaviorStepRole
{
    Setup,
    Trigger,
    Filter,
    Action,
    Fake,
    Invoke,
    Assert,
}

[GenerateSerializer]
[Alias("db.behavior.diagnostic.v1")]
public sealed record BehaviorDiagnostic(
    [property: Id(0)] string Code,
    [property: Id(1)] BehaviorDiagnosticSeverity Severity,
    [property: Id(2)] string Message,
    [property: Id(3)] int Line,
    [property: Id(4)] int Column);

[GenerateSerializer]
[Alias("db.behavior.step-call.v1")]
public sealed record BehaviorStepCall(
    [property: Id(0)] string Keyword,
    [property: Id(1)] string Text,
    [property: Id(2)] string Binding,
    [property: Id(3)] BehaviorStepRole Role,
    [property: Id(4)] IReadOnlyList<string> Arguments,
    [property: Id(5)] int Line);

[GenerateSerializer]
[Alias("db.behavior.scenario-plan.v1")]
public sealed record BehaviorScenarioPlan(
    [property: Id(0)] string Name,
    [property: Id(1)] string TriggerKey,
    [property: Id(2)] IReadOnlyList<BehaviorStepCall> Steps);

[GenerateSerializer]
[Alias("db.behavior.test-plan.v1")]
public sealed record BehaviorTestPlan(
    [property: Id(0)] string Name,
    [property: Id(1)] IReadOnlyList<BehaviorStepCall> Steps);

[GenerateSerializer]
[Alias("db.behavior.plan.v1")]
public sealed record BehaviorPlan(
    [property: Id(0)] string Feature,
    [property: Id(1)] string SourceHash,
    [property: Id(2)] IReadOnlyList<BehaviorScenarioPlan> Behaviors,
    [property: Id(3)] IReadOnlyList<BehaviorTestPlan> Tests);

[GenerateSerializer]
[Alias("db.behavior.compilation.v1")]
public sealed record BehaviorCompilation(
    [property: Id(0)] BehaviorPlan? Plan,
    [property: Id(1)] IReadOnlyList<BehaviorDiagnostic> Diagnostics)
{
    public bool Success => Plan is not null
        && Diagnostics.All(static diagnostic => diagnostic.Severity != BehaviorDiagnosticSeverity.Error);
}

[GenerateSerializer]
[Alias("db.behavior.step-suggestion.v1")]
public sealed record BehaviorStepSuggestion(
    [property: Id(0)] string Keyword,
    [property: Id(1)] string Template,
    [property: Id(2)] string Description);

public interface IBehaviorCompiler
{
    BehaviorCompilation Compile(string source);

    IReadOnlyList<BehaviorStepSuggestion> Suggestions { get; }
}

[GenerateSerializer]
[Alias("db.behavior.generation.v1")]
public sealed record BehaviorGeneration(
    [property: Id(0)] string Source,
    [property: Id(1)] BehaviorCompilation Compilation,
    [property: Id(2)] string Model);

public interface IBehaviorFeatureGenerator
{
    Task<BehaviorGeneration> Generate(string request, CancellationToken cancellationToken = default);
}

[GenerateSerializer]
[Alias("db.behavior.summary.v1")]
public sealed record BehaviorSummary(
    [property: Id(0)] string Name,
    [property: Id(1)] string Title,
    [property: Id(2)] string Source,
    [property: Id(3)] bool Active,
    [property: Id(4)] BehaviorTestReport? LastTest,
    [property: Id(5)] IReadOnlyList<BehaviorDiagnostic> Diagnostics);

[GenerateSerializer]
[Alias("db.behavior.event.v1")]
public sealed record BehaviorEvent(
    [property: Id(0)] string EventId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Source,
    [property: Id(3)] string Text,
    [property: Id(4)] double Value,
    [property: Id(5)] string SourceUri,
    [property: Id(6)] DateTimeOffset OccurredAt)
{
    public string TriggerKey => Kind.Equals("x.post", StringComparison.OrdinalIgnoreCase)
        ? $"x.post/account:{Source.Trim().ToLowerInvariant()}"
        : $"{Kind.Trim().ToLowerInvariant()}/source:{Source.Trim().ToLowerInvariant()}";
}

[GenerateSerializer]
[Alias("db.behavior.subscription.v1")]
public sealed record BehaviorSubscription(
    [property: Id(0)] string Owner,
    [property: Id(1)] string BehaviorName,
    [property: Id(2)] string ScenarioName,
    [property: Id(3)] string RevisionHash);

[GenerateSerializer]
[Alias("db.behavior.test-report.v1")]
public sealed record BehaviorTestReport(
    [property: Id(0)] bool AllGreen,
    [property: Id(1)] IReadOnlyList<string> Failures,
    [property: Id(2)] int Scenarios);

[GenerateSerializer]
[Alias("db.behavior.definition-state.v1")]
public sealed record BehaviorDefinitionState(
    [property: Id(0)] string Source,
    [property: Id(1)] BehaviorCompilation Compilation,
    [property: Id(2)] bool Active,
    [property: Id(3)] BehaviorTestReport? LastTest);

[GenerateSerializer]
[Alias("db.behavior.catalog-state.v1")]
public sealed record BehaviorCatalogState(
    [property: Id(0)] IReadOnlyList<string> Names);

[GenerateSerializer]
[Alias("db.behavior.directory-stats.v1")]
public sealed record BehaviorDirectoryStats(
    [property: Id(0)] int SubscriptionCount,
    [property: Id(1)] int ActivePartitions,
    [property: Id(2)] int ConfiguredPartitions);

public static class BehaviorRouting
{
    public const int PartitionCount = 64;
}

public static class BehaviorIngressNames
{
    public const string Shared = "shared";
}
