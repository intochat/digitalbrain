using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Registry;

internal static class RegistryEndpoints
{
    private const string UnmetIntentsPath = "/discovery/unmet-intents";
    private const string BoardKey = "unmet-intents";

    public static void MapRegistryBoard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(UnmetIntentsPath, static async Task<IResult> (IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            var items = await grains.GetGrain<IUnmetIntentBoard>(BoardKey).Read().WaitAsync(cancellationToken);
            return Results.Ok(items);
        });
    }
}