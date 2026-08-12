namespace DigitalBrain.Abstractions;

// Watermarked, resumable owner (or principal) projection of durable story facts.
[ClientEntryPoint]
[Alias("db.corpus")]
public partial interface ICorpus :
    INeuron,
    IHandle<AppendCorpusEntry>,
    IHandle<ReadCorpus>,
    IHandle<ReadEpisode>
{
    const string GrainTypeName = "corpus";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    static NeuronId ForPrincipal(OwnerId owner, PrincipalId principal)
        => new(GrainTypeName, owner, PrincipalPartition.InstanceName(principal, InstanceName));
}
