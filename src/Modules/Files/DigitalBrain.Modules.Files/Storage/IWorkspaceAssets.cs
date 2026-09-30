using DigitalBrain.Contracts;
using Orleans;

namespace DigitalBrain.Files;

[Alias("intochat.workspace-assets"), Orleans.Metadata.DefaultGrainType("intochat.workspace-assets")]
internal interface IWorkspaceAssets : INeuron
{
    Task<WorkspaceAsset[]> List();
    Task<ImageAsset> Register(WorkspaceAsset asset);
    Task<ImageAsset> Read(string documentId);
}
