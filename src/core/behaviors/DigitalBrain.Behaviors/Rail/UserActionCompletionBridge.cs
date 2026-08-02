using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

public static class UserActionCompletionBridge
{
    public const string GrainTypeName = "user-action-completion-bridge";

    public static NeuronId For(OwnerId owner, Guid actionEpoch)
    {
        if (owner == default)
        {
            throw new ArgumentException("Owner is required.", nameof(owner));
        }

        if (actionEpoch == Guid.Empty)
        {
            throw new ArgumentException("Action epoch is required.", nameof(actionEpoch));
        }

        return new NeuronId(GrainTypeName, owner, actionEpoch.ToString("N"));
    }
}

[GenerateSerializer]
[Alias("behaviors.bind-user-action-completion")]
public sealed record BindUserActionCompletion(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] AttemptId Attempt,
    [property: Id(2)] NeuronId Module,
    [property: Id(3)] string ModuleId,
    [property: Id(4)] ProtectedPayloadReference ActionReference,
    [property: Id(5)] Guid ActionEpoch,
    [property: Id(6)] long ParkRevision,
    [property: Id(7)] DateTimeOffset ExpiresAt,
    [property: Id(8)] CommandId AuthorizationCommandId,
    [property: Id(9)] string ServerKey,
    [property: Id(10)] string AuthorizationState) : Synapse;
