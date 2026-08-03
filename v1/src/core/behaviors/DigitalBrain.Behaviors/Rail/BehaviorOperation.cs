using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

[GenerateSerializer]
[Alias("db.behavior.operation-identity")]
public sealed record BehaviorOperationIdentity
{
    public BehaviorOperationIdentity(NeuronId task, AttemptId attempt, int sequence)
    {
        if (task == default || string.IsNullOrWhiteSpace(task.Type) || string.IsNullOrWhiteSpace(task.Name))
        {
            throw new ArgumentException("Operation identity requires a non-default Task neuron id.", nameof(task));
        }

        var taskGrainType = NeuronId.GrainTypeNameOf(typeof(ITask));
        if (!string.Equals(task.Type, taskGrainType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Operation identity Task grain type must be '{taskGrainType}'.",
                nameof(task));
        }

        if (attempt == default || attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("Operation identity requires a non-default Attempt id.", nameof(attempt));
        }

        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Operation sequence must be non-negative.");
        }

        Task = task;
        Attempt = attempt;
        Sequence = sequence;
    }

    [Id(0)]
    public NeuronId Task { get; }

    [Id(1)]
    public AttemptId Attempt { get; }

    [Id(2)]
    public int Sequence { get; }
}

[GenerateSerializer]
[Alias("db.behavior.capability-edge")]
public sealed record BehaviorCapabilityEdge
{
    public BehaviorCapabilityEdge(
        NeuronId target,
        string requestSynapseId,
        int requestSchemaVersion,
        string responseSynapseId,
        int responseSchemaVersion)
    {
        if (target == default || string.IsNullOrWhiteSpace(target.Type) || string.IsNullOrWhiteSpace(target.Name))
        {
            throw new ArgumentException("Capability edge requires a non-default target neuron id.", nameof(target));
        }

        if (string.IsNullOrWhiteSpace(requestSynapseId))
        {
            throw new ArgumentException("Capability edge requires a request synapse id.", nameof(requestSynapseId));
        }

        if (requestSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestSchemaVersion),
                requestSchemaVersion,
                "Request schema version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(responseSynapseId))
        {
            throw new ArgumentException("Capability edge requires a response synapse id.", nameof(responseSynapseId));
        }

        if (responseSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseSchemaVersion),
                responseSchemaVersion,
                "Response schema version must be positive.");
        }

        Target = target;
        RequestSynapseId = requestSynapseId;
        RequestSchemaVersion = requestSchemaVersion;
        ResponseSynapseId = responseSynapseId;
        ResponseSchemaVersion = responseSchemaVersion;
    }

    [Id(0)]
    public NeuronId Target { get; }

    [Id(1)]
    public string RequestSynapseId { get; }

    [Id(2)]
    public int RequestSchemaVersion { get; }

    [Id(3)]
    public string ResponseSynapseId { get; }

    [Id(4)]
    public int ResponseSchemaVersion { get; }
}

[GenerateSerializer]
[Alias("db.behavior.operation")]
public sealed record BehaviorOperation
{
    public BehaviorOperation(
        BehaviorOperationIdentity identity,
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        TaskOperationPhase phase,
        ProtectedPayloadReference? responsePayload = null,
        string? redactedSummary = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(edge);
        ValidateReference(requestPayload, nameof(requestPayload));
        ValidatePhaseAndResponse(phase, responsePayload);

        if (redactedSummary is not null && string.IsNullOrWhiteSpace(redactedSummary))
        {
            throw new ArgumentException("Redacted summary cannot be blank when provided.", nameof(redactedSummary));
        }

        Identity = identity;
        Edge = edge;
        RequestPayload = requestPayload;
        Phase = phase;
        ResponsePayload = responsePayload;
        RedactedSummary = redactedSummary;
    }

    [Id(0)]
    public BehaviorOperationIdentity Identity { get; }

    [Id(1)]
    public BehaviorCapabilityEdge Edge { get; }

    [Id(2)]
    public ProtectedPayloadReference RequestPayload { get; }

    [Id(3)]
    public TaskOperationPhase Phase { get; }

    [Id(4)]
    public ProtectedPayloadReference? ResponsePayload { get; }

    [Id(5)]
    public string? RedactedSummary { get; }

    internal static void ValidateReference(ProtectedPayloadReference reference, string paramName)
    {
        if (reference.Id == Guid.Empty)
        {
            throw new ArgumentException("Protected payload reference cannot be empty.", paramName);
        }

        if (reference.ExpiresAt is { } expiresAt && expiresAt == default)
        {
            throw new ArgumentException("Protected payload reference expiry cannot be default when set.", paramName);
        }
    }

    internal static void ValidatePhaseAndResponse(
        TaskOperationPhase phase,
        ProtectedPayloadReference? responsePayload)
    {
        switch (phase)
        {
            case TaskOperationPhase.Completed:
                if (responsePayload is null)
                {
                    throw new ArgumentException(
                        "Completed operations require a response payload reference.",
                        nameof(responsePayload));
                }

                ValidateReference(responsePayload.Value, nameof(responsePayload));
                break;

            case TaskOperationPhase.Prepared:
            case TaskOperationPhase.Dispatched:
            case TaskOperationPhase.Uncertain:
                if (responsePayload is not null)
                {
                    throw new ArgumentException(
                        $"{phase} operations cannot carry a response payload reference.",
                        nameof(responsePayload));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown task operation phase.");
        }
    }
}
