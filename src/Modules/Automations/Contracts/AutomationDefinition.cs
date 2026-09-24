namespace DigitalBrain.Automations;

public enum AutomationTriggerKind
{
    Schedule = 0,
    Event = 1,
    OnDemand = 2,
}

[GenerateSerializer, Alias("automations.trigger")]
public sealed record AutomationTrigger
{
    [Id(0)] public required AutomationTriggerKind Kind { get; init; }

    // Schedule: the durable reminder period in seconds.
    [Id(1)] public int IntervalSeconds { get; init; }

    // Event: the neuron that publishes the trigger signal and the signal's alias.
    [Id(2)] public string? EventSourceId { get; init; }
    [Id(3)] public string? EventSignalType { get; init; }
}

[GenerateSerializer, Alias("automations.action")]
public sealed record AutomationAction
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Operation { get; init; }
    [Id(2)] public decimal EstimatedCompute { get; init; }
    [Id(3)] public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>();
}

[GenerateSerializer, Alias("automations.definition")]
public sealed record AutomationDefinition
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string WorkspaceId { get; init; }
    [Id(2)] public required string Name { get; init; }
    [Id(3)] public required AutomationTrigger Trigger { get; init; }
    [Id(4)] public required AutomationAction Action { get; init; }
    [Id(5)] public required decimal ComputeBudget { get; init; }
}

[GenerateSerializer, Alias("automations.operation")]
public sealed record AutomationOperation
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Operation { get; init; }
    [Id(2)] public bool ReadOnly { get; init; }
    [Id(3)] public string? MeterId { get; init; }
    [Id(4)] public decimal EstimatedCompute { get; init; }
}

[GenerateSerializer, Alias("automations.action-result")]
public sealed record AutomationActionResult
{
    [Id(0)] public required bool Succeeded { get; init; }
    [Id(1)] public string? Error { get; init; }
    [Id(2)] public decimal ActualCompute { get; init; }
}

[GenerateSerializer, Alias("automations.validation")]
public sealed record AutomationValidationResult
{
    [Id(0)] public required bool Valid { get; init; }
    [Id(1)] public string? Reason { get; init; }

    public static AutomationValidationResult Ok { get; } = new() { Valid = true };
    public static AutomationValidationResult Rejected(string reason) => new() { Valid = false, Reason = reason };
}

[GenerateSerializer, Alias("automations.snapshot")]
public sealed record AutomationSnapshot
{
    [Id(0)] public required AutomationDefinition Definition { get; init; }
    [Id(1)] public required bool Active { get; init; }
    [Id(2)] public required decimal SpentCompute { get; init; }
    [Id(3)] public required IReadOnlyList<JobRun> Runs { get; init; }
}
