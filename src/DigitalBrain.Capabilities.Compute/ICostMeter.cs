using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Capabilities.Compute;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Carries the exhausted trace id.")]
public sealed class BudgetExhaustedException(string traceId)
    : Exception($"Trace {traceId} has no budget left.")
{
    public string TraceId { get; } = traceId;
}

public interface ICostMeter
{
    ValueTask<Cost> RecordAsync(CostKind kind, string item, decimal quantity, string unit, Funding funding,
        string costId, CancellationToken cancellationToken, CostSubject? subject = null);

    ValueTask<ExternalCallHandle> BeginExternalCallAsync(ExternalCallRequest request, CancellationToken cancellationToken);
    ValueTask MarkExternalCallDispatchStartedAsync(ExternalCallHandle handle, CancellationToken cancellationToken);
    ValueTask<ExternalCallCompletion> CompleteExternalCallAsync(
        ExternalCallHandle handle, ExternalCallOutcome outcome, CancellationToken cancellationToken);
}
