namespace DigitalBrain.Abstractions;

// Per-owner grant book: principals may grant read/watch on their subjects to others.
[ClientEntryPoint]
[Alias("db.grants")]
public partial interface IGrants :
    INeuron,
    IHandle<GrantAccess>,
    IHandle<RevokeAccess>,
    IHandle<ListGrants>
{
    const string GrainTypeName = "grants";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    static NeuronId ForPrincipal(OwnerId owner, PrincipalId principal)
        => new(GrainTypeName, owner, PrincipalPartition.InstanceName(principal, InstanceName));

    [Alias(nameof(HasAccess))]
    Task<bool> HasAccess(PrincipalId grantee, NeuronId subject, GrantKind kind);
}
