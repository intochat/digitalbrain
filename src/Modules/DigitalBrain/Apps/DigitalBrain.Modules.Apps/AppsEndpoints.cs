using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Apps;

internal static class AppsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var apps = BrainRoutes.Group(endpoints, "/apps");
        apps.MapGet("", (IDigitalBrain brain, CancellationToken ct) => AppHttp.Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            var installed = await brain.Get<IAppCatalog>(scope).List().WaitAsync(ct);
            var manifests = installed.Select(installation => installation.Manifest)
                .OrderBy(manifest => manifest.Name, StringComparer.Ordinal)
                .Select(manifest => new
                {
                    id = manifest.Id,
                    name = manifest.Name,
                    description = manifest.DescriptionForPeople,
                    kind = manifest.Kind.ToString().ToLowerInvariant(),
                    uiEntry = manifest.UiEntry,
                    examplePrompts = manifest.ExamplePrompts,
                    permissions = manifest.Permissions.Select(permission => new
                    {
                        semanticTypeId = permission.SemanticTypeId,
                        reason = permission.Reason,
                        write = permission.Write,
                    }),
                    meters = manifest.Meters.Select(meter => new
                    {
                        meterId = meter.MeterId,
                        unit = meter.Unit,
                        aggregation = meter.Aggregation,
                        proposedPriceInCompute = meter.ProposedPriceInCompute,
                    }),
                })
                .ToArray();
            return Results.Ok(manifests);
        }));
        apps.MapGet("/{appId}/consent", (string appId, IDigitalBrain brain, CancellationToken ct) => AppHttp.Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            var sheet = await brain.Get<IAppConsent>(scope).Review(appId).WaitAsync(ct);
            return Results.Ok(sheet);
        }));
        apps.MapPost("/{appId}/consent/approve", (string appId, IDigitalBrain brain, CancellationToken ct) => AppHttp.Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            var sheet = await brain.Get<IAppConsent>(scope).Approve(appId).WaitAsync(ct);
            return Results.Ok(sheet);
        }));
    }
}
