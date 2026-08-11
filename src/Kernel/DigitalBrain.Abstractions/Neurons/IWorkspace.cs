namespace DigitalBrain.Abstractions;

[ClientEntryPoint]
[Alias("db.workspace")]
public partial interface IWorkspace :
    INeuron,
    IHandle<AddMember>,
    IHandle<ChangeRole>,
    IHandle<RemoveMember>,
    IHandle<ReadMembership>
{
    const string GrainTypeName = "workspace";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);
}
