namespace DigitalBrain.Abstractions.Library;

// Owner-wide published artifact catalog (Library 2b (a): grant-gated publish/discover residual).
// Installs land in the caller's principal partition (disabled until enable).
// Content-hashed, immutable versions. Do not principal-partition the catalog grain.
[ClientEntryPoint]
[Alias("db.library")]
public partial interface ILibrary :
    INeuron,
    IHandle<PublishLibraryArtifact>,
    IHandle<DiscoverLibrary>,
    IHandle<InstallLibraryArtifact>,
    IHandle<ListLibraryInstalls>,
    IHandle<EnableLibraryInstall>
{
    const string GrainTypeName = "library";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);
}
