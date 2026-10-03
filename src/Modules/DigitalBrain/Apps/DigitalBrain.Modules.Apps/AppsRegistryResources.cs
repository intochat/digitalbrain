using System.Text.Json;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;

namespace DigitalBrain.Apps;

internal sealed class AppsRegistryResources(IDigitalBrain brain) : IRegistryResourceProvider
{
    public string Id => "apps";
    public async Task<RegistryDiscovery> Discover(CancellationToken cancellationToken)
    {
        var installed = await InstalledApps.List(brain, BrainScope.CurrentId(), cancellationToken);
        return new([.. installed.Select(app => new RegistryCapability("app:" + app.Id,
            app.Title + ": " + app.Description,
            [.. app.App.Operations.Select(operation => AppToolName.For(app.Package, operation.Name))],
            JsonSerializer.Serialize(new { app.Id, app.Title, app.Description, app.App.Operations })))], []);
    }
}
