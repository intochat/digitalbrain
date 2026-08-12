namespace DigitalBrain.Abstractions;

// Bounded repo rail: open a root, list/read files for behavior review (Wave 8).
[ClientEntryPoint]
[Alias("db.repository")]
public partial interface IRepository :
    INeuron,
    IHandle<OpenRepository>,
    IHandle<ListRepositoryFiles>,
    IHandle<ReadRepositoryFile>
{
    const string GrainTypeName = "repository";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);
}
