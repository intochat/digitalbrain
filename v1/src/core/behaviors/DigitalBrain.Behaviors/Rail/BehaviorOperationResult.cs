using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

[GenerateSerializer]
[Alias("db.behavior.operation-result")]
public sealed record BehaviorOperationResult
{
    public BehaviorOperationResult(
        BehaviorOperationIdentity identity,
        TaskOperationPhase phase,
        ProtectedPayloadReference? responsePayload = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BehaviorOperation.ValidatePhaseAndResponse(phase, responsePayload);

        Identity = identity;
        Phase = phase;
        ResponsePayload = responsePayload;
    }

    [Id(0)]
    public BehaviorOperationIdentity Identity { get; }

    [Id(1)]
    public TaskOperationPhase Phase { get; }

    [Id(2)]
    public ProtectedPayloadReference? ResponsePayload { get; }
}
