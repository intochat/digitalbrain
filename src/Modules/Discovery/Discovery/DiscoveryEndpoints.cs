using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Discovery;

internal static class DiscoveryEndpoints
{
    private const string UnmetIntentsPath = "/discovery/unmet-intents";
    private const string BoardKey = "unmet-intents";

    public static void MapDiscoveryBoard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(UnmetIntentsPath, static async Task<IResult> (IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            var items = await grains.GetGrain<IUnmetIntentBoard>(BoardKey).Read().WaitAsync(cancellationToken);
            return Results.Ok(items);
        });
    }
}
