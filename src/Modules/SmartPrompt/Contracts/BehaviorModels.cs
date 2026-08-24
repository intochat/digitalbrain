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
