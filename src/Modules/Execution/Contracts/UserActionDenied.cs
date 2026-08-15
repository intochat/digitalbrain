using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.user-action-denied")]
public sealed record UserActionDenied : Failure
{
    public UserActionDenied(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ModuleId = moduleId.Trim();
    }

    [Id(0)]
    public string ModuleId { get; init; }
}

