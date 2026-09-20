using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("changeset")]
public interface IChangeSet : INeuron
{
    Task<ChangeSetReceipt> Propose(ProposeEdit request);

    Task<ChangeSetReceipt> Check();

    Task<ChangeSetReceipt> Commit(CommitChangeSet request);

    Task<ChangeSetReceipt> Discard();

    [ReadOnly]
    Task<ChangeSetSnapshot> Read();
}