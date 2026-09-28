using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Discovery;

namespace DigitalBrain.Apps;

// The manifest directory already merges first-party manifests with every saved or installed app.
internal sealed class AppCapabilitySource(IDigitalBrain brain) : ICapabilitySource
{
    private IAppManifestDirectory Directory => brain.Get<IAppManifestDirectory>(AppManifestDirectoryGrains.Key);

    public async Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Describe(await Directory.Read().WaitAsync(cancellationToken).ConfigureAwait(false));

    public async Task Watch(Action changed, CancellationToken cancellationToken)
    {
        await using var changes = await brain.SubscribeAsync<AppCatalogued>(Directory, cancellationToken).ConfigureAwait(false);
        await foreach (var _ in changes.ReadAllAsync(cancellationToken).ConfigureAwait(false)) { changed(); }
    }

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
