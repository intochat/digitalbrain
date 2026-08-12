using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.library-state")]
internal sealed class LibraryState
{
    [Id(0)]
    public List<LibraryArtifact> Artifacts { get; set; } = [];

    [Id(1)]
    public List<LibraryInstall> Installs { get; set; } = [];
}
