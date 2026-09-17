using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("changeset")]
public interface IChangeSet : INeuron
{
    [Alias("propose")]
    [NeuronTool]
    Task<Accepted<ChangeSetReceipt>> Propose(ProposeEdit command);

    [Alias("check")]
    [NeuronTool]
    Task<Accepted<ChangeSetReceipt>> Check(CheckChangeSet command);

    [Alias("commit")]
    [NeuronTool]
    Task<Accepted<ChangeSetReceipt>> Commit(CommitChangeSet command);

    [Alias("discard")]
    [NeuronTool]
    Task<Accepted<ChangeSetReceipt>> Discard(DiscardChangeSet command);

    [ReadOnly]
    [Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<ChangeSetSnapshot> Read();
}
