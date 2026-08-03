using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Runtime;

[ClientEntryPoint]
internal interface IBehaviorTaskAuthority : IGrainWithStringKey
{
    [Alias(nameof(ReadValidatedTask))]
    Task<TaskSnapshot> ReadValidatedTask(
        NeuronId task,
        AttemptId attempt,
        bool requireActivation,
        CancellationToken cancellationToken);
}

internal static class BehaviorTaskAuthority
{
    internal const string GrainTypeName = "behavior-task-authority";
    internal const string InstanceName = "authority";

    internal static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);
}
