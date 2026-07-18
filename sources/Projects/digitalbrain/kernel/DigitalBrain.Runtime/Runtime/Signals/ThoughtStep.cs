using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Runtime.Runtime.Signals;

/// <summary>
/// Structured system-level broadcast signal emitted by the VM interpreter at every reasoning step.
/// </summary>
[Signal(Fqn)]
[GenerateSerializer]
public sealed record ThoughtStep(
    [property: Id(0)] Guid CorrelationId,
    [property: Id(1)] Guid StepId,
    [property: Id(2)] Guid? ParentStepId,
    [property: Id(3)] string NeuronFqn,
    [property: Id(4)] string BranchName,
    [property: Id(5)] string ActionType, // "SpeculateStart", "VerifyPass", "VerifyFail", "AskCall", "Commit", "Rollback"
    [property: Id(6)] string Description,
    [property: Id(7)] double Confidence,
    [property: Id(8)] string StateSummary,
    [property: Id(9)] DateTimeOffset Timestamp
)
{
    public const string Fqn = "DigitalBrain.Kernel.ThoughtStep";
}
