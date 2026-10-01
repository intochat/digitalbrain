using DigitalBrain.Contracts;
using Orleans;

namespace DigitalBrain.Files;

[Alias("intochat.workspace-asset-page"), Orleans.Metadata.DefaultGrainType("intochat.workspace-asset-page")]
internal interface IWorkspaceAssetPage : INeuron
{
    Task<WorkspaceAsset[]> List();
    Task<ImageAsset?> Read(string documentId);
    Task<bool> HasCapacity();
    Task<ImageAsset> Register(WorkspaceAsset asset);
}
