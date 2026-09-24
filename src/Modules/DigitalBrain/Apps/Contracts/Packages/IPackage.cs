using DigitalBrain.Contracts;
using Orleans.Concurrency;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// A shareable behavior, keyed "owner/name". Reads are public; changes require the owner.
// Reads interleave because packages read each other while forking, merging and accepting.
[Alias("apps.package"), DefaultGrainType("apps.package")]
public interface IPackage : INeuron
{
    [AlwaysInterleave] Task<PackageSnapshot> Read();
    [AlwaysInterleave] Task<PackageRevision> ReadRevision(string revision);
    Task<PackageRevision> Commit(CommitPackage request);
    Task<PackageSnapshot> Fork(ForkPackage request);
    Task<PackageSnapshot> Pull(PullPackage request);
    Task<PackageProposal> Propose(ProposeChange request);
    Task<PackageSnapshot> Accept(AcceptProposal request);
    Task<PackageSnapshot> Publish(PublishPackage request);
}
