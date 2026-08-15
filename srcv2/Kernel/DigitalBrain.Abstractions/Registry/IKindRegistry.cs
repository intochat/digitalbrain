namespace DigitalBrain.Abstractions.Registry;

// Durable catalog of kind records for the cell tier (Wave 6).
// Built-in calculator remains hard-wired; registry holds installed metadata.
[ClientEntryPoint]
[Alias("db.kind-registry")]
public partial interface IKindRegistry :
    INeuron,
    IHandle<InstallKind>,
    IHandle<ListKinds>
{
    const string GrainTypeName = "kindregistry";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);
}
