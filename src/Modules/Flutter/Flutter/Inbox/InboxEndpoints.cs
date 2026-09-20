using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter.Inbox;

internal static class InboxEndpoints
{
    public static void MapInbox(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(FlutterModule.InboxPath, static async Task<IResult> (IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            var lines = await grains.GetGrain<IInbox>(FlutterModule.InboxGrain).Read().WaitAsync(cancellationToken);
            return Results.Ok(lines);
        });
    }
}
