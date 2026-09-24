using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

/// <summary>The installation seam: packages and local runtime state stay separate.</summary>
public sealed class WorkspaceApps(IDigitalBrain brain)
{
    public static string Key(string workspace, string appId) => "app-" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(workspace + "\0" + appId)));

    public IWorkspaceApp Get(string workspace, string appId) => brain.Get<IWorkspaceApp>(Key(workspace, appId));

    public async Task<AppSnapshot> Install(string workspace, AppManifest manifest, IReadOnlyDictionary<string, string> configuration)
    {
        var snapshot = await Get(workspace, manifest.Id).Install(manifest, configuration);
        // A retry repairs an interrupted catalog projection without resetting the app instance.
        await brain.Get<IAppCatalog>(workspace).Install(manifest);
        if (manifest.Composition!.Activation == AppActivation.WithWorkspace)
        {
            var activation = await brain.Get<IWorkspaceLifecycle>(workspace).Read();
            if (activation.Active) { return await Get(workspace, manifest.Id).WorkspaceActivated(activation.Generation); }
        }
        return snapshot;
    }

    public async Task<IReadOnlyList<AppSnapshot>> List(string workspace)
    {
        var installed = await brain.Get<IAppCatalog>(workspace).List();
        var results = new List<AppSnapshot>();
        foreach (var item in installed.Where(item => item.Manifest.Composition is not null))
        { results.Add(await Get(workspace, item.Manifest.Id).Read()); }
        return results;
    }

    public async Task<IReadOnlyList<AppSnapshot>> ActivateWorkspace(string workspace)
    {
        var lifecycle = brain.Get<IWorkspaceLifecycle>(workspace);
        // Publish the durable activation before enumerating installations. An installation
        // racing this pass then either appears here or observes the active generation itself.
        var activation = await lifecycle.Activate();
        var generation = activation.Generation;
        foreach (var app in await List(workspace))
        {
            if (app.Manifest.Composition!.Activation == AppActivation.WithWorkspace && app.Status != AppStatus.Stopped)
            { await Get(workspace, app.Manifest.Id).WorkspaceActivated(generation); }
        }
        return await List(workspace);
    }
}
