using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

public interface IUserActionCustody
{
    ValueTask<IssuedUserAction> IssueAsync(
        OwnerId owner,
        NeuronId execution,
        AttemptId attempt,
        NeuronId moduleNeuron,
        string moduleId,
        string displayText,
        ReadOnlyMemory<byte> actionMaterial,
        long parkRevision,
        TimeSpan lifetime,
        NeuronId completer,
        Guid actionEpoch,
        CancellationToken cancellationToken);
}
