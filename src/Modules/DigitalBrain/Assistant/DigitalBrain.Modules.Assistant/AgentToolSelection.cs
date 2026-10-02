using DigitalBrain.Apps;

namespace DigitalBrain.Assistant;

internal sealed class AgentToolSelection(IGrainFactory grains)
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string scope, CancellationToken ct)
    {
        try
        {
            var installed = await InstalledApps.List(grains, scope, ct);
            return installed.SelectMany(app => app.App.Operations.Select(operation => operation.Name))
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [];
        }
    }
}
