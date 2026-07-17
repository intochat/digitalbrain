using Brain.Contracts;

namespace Brain.Modules.Behaviors;

public interface IBehavior : INeuronContract
{
    static string ContractDescription => "Owner-scoped self-evolution lifecycle neuron binding grants to a content-hash identity.";

    [NeuronContract("behavior.propose.v1")]
    Task<BehaviorReply> ProposeAsync(BehaviorProposal proposal);

    [NeuronContract("behavior.approve.v1")]
    Task<BehaviorReply> ApproveAsync(BehaviorApproval approval);

    [NeuronContract("behavior.decline.v1")]
    Task<BehaviorReply> DeclineAsync(BehaviorDecline decline);

    [NeuronContract("behavior.rollback.v1")]
    Task<BehaviorReply> RollbackAsync(BehaviorRollback rollback);
}

public sealed record BehaviorGrant(string Address, string Contract);
public sealed record BehaviorProposal(string Source, string SourceHash, bool BddPassed, BehaviorGrant[] Grants);
public sealed record BehaviorApproval(string SourceHash, string GrantsHash);
public sealed record BehaviorDecline(string SourceHash, string Reason);
public sealed record BehaviorRollback(string SourceHash);
public sealed record BehaviorReply(string Status, string? SourceHash = null, string? Identity = null, string? GrantsHash = null);
