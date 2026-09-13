using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("changeset")]
public interface IChangeSet : INeuron
{
    [Alias("propose")]
    Task<Accepted<ChangeSetReceipt>> Propose(ProposeEdit command);

    [Alias("check")]
    Task<Accepted<ChangeSetReceipt>> Check(CheckChangeSet command);

    [Alias("commit")]
    Task<Accepted<ChangeSetReceipt>> Commit(CommitChangeSet command);

    [Alias("discard")]
    Task<Accepted<ChangeSetReceipt>> Discard(DiscardChangeSet command);

    [ReadOnly]
    [Alias("read")]
    Task<ChangeSetSnapshot> Read();
}
