using DigitalBrain.Discovery;

namespace DigitalBrain.Apps;

// The manifest directory already merges first-party manifests with every saved or installed app.
internal sealed class AppCapabilitySource(IGrainFactory grains) : ICapabilitySource
{
    public async Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Describe(await grains.GetGrain<IAppManifestDirectory>(AppManifestDirectoryGrains.Key).Read().WaitAsync(cancellationToken)
            .ConfigureAwait(false));

    public static IReadOnlyList<CapabilityDocument> Describe(IEnumerable<ScopedAppManifest> manifests)
        => [.. manifests.SelectMany(static scoped =>
        {
            var manifest = scoped.Manifest;
            var workspaceId = scoped.OwningWorkspaceId;
            return manifest.Operations
                .Select(operation => new CapabilityDocument(manifest.Id + "/" + operation.Name, CapabilityKind.Operation,
                    operation.Name, operation.DescriptionForModel, workspaceId, manifest.AgentTools))
                .Prepend(new CapabilityDocument(manifest.Id, CapabilityKind.App, manifest.Name,
                    string.Join(' ', manifest.DescriptionForPeople, manifest.DescriptionForModel), workspaceId, manifest.AgentTools));
        })];
}
