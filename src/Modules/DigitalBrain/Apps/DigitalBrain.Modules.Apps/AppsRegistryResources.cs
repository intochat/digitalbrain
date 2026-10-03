using System.Text.Json;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;

namespace DigitalBrain.Apps;

internal sealed class AppsRegistryResources(IDigitalBrain brain) : IRegistryResourceProvider
{
    public string Id => "apps";
    public RegistryCapability Summary => new(Id, "Installed apps and their operations. Browse provider 'apps' to choose an app.", []);
    public Task<RegistryDiscovery> Discover(CancellationToken ct) => Browse(0, 10, ct);

    public async Task<RegistryDiscovery> Browse(int offset, int limit, CancellationToken ct)
    {
        var page = await brain.Get<IApps>(BrainScope.CurrentId()).Page(offset, limit + 1).WaitAsync(ct);
        if (!page.Ready) { return new([], [new(Id, "The legacy app index is being updated. Retry this page.")], offset); }
        var packages = page.Packages;
        var capabilities = new List<RegistryCapability>();
        var errors = new List<RegistryDiscoveryError>();
        foreach (var package in packages.Take(limit))
        {
            try { if (await Select("apps:" + package, ct) is { } capability) { capabilities.Add(capability); } }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { errors.Add(new(Id, "An installed app could not be described.")); }
        }
        return new([.. capabilities], [.. errors], packages.Length > limit ? offset + limit : null);
    }

    public async Task<RegistryCapability?> Select(string id, CancellationToken ct)
    {
        if (!id.StartsWith("apps:", StringComparison.Ordinal)) { return null; }
        var package = PackageId.Parse(id[5..]);
        var app = await brain.Get<IApp>(BrainScope.CurrentId() + "/packages/" + package).Read().WaitAsync(ct);
        if (app.Status != AppStatus.Installed || app.UninstallPending || app.Revision is not { } revision) { return null; }
        var manifest = (await brain.Get<IPackage>(package.ToString()).ReadRevision(revision.Revision).WaitAsync(ct)).Content.Manifest;
        return new(id, manifest.Title + ": " + manifest.Description,
            [.. app.Operations.Select(operation => AppToolName.For(package, operation.Name))],
            JsonSerializer.Serialize(new { package, app.Operations }), "apps", package + "@" + revision.Revision);
    }
}
