namespace DigitalBrain.Abstractions;

// Durable per-owner catalog of instances that exist even when cold (not activated).
// get_neurons reads this so "walk me through this brain" can name idle/disabled/cold cells.
// Named IRegistry so GrainTypeNameOf → "registry" matches [GrainType("registry")].
[ClientEntryPoint]
[Alias("db.registry")]
public partial interface IRegistry :
    INeuron,
    IHandle<RegisterInstance>,
    IHandle<RetireInstance>,
    IHandle<SetInstanceEnabled>,
    IHandle<InstallBundle>,
    IHandle<ListInstances>
{
    const string GrainTypeName = "registry";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    // A18: registry partition per principal (cold charts/schedules stay private).
    static NeuronId ForPrincipal(OwnerId owner, PrincipalId principal)
        => new(GrainTypeName, owner, PrincipalPartition.InstanceName(principal, InstanceName));
}
