using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.Aspire;

internal static class AspireBridgeEndpoints
{
    public static void MapAspireBridge(this IEndpointRouteBuilder endpoints)
    {
        var bridge = endpoints.MapGroup(AspireBridgeRoutes.Root).AddEndpointFilter(RequireBridgeKey).ExcludeFromDescription();
        bridge.MapGet("commands", (AspireBridge commands, CancellationToken cancellationToken)
            => TypedResults.ServerSentEvents(commands.ReadCommandsAsync(cancellationToken), "command"));
        bridge.MapPost("commands/{id:guid}", (Guid id, AspireBridgeCommandResult result, AspireBridge commands) =>
        {
            commands.Complete(id, result);
            return TypedResults.NoContent();
        });
        bridge.MapPost("resources", async (AspireResource resource, IGrainFactory grains, IOptions<AspireOptions> options) =>
        {
            await grains.GetGrain<IAspireResourceReporter>(options.Value.ApplicationName).Report(resource);
            return TypedResults.NoContent();
        });
        bridge.MapPost("resources/sync", async (AspireResource[] resources, IGrainFactory grains, IOptions<AspireOptions> options) =>
        {
            await grains.GetGrain<IAspireResourceReporter>(options.Value.ApplicationName).ReportMany(resources);
            return TypedResults.NoContent();
        });
    }

    private static async ValueTask<object?> RequireBridgeKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[AspireModule.ConfigurationRoot + ":BridgeKey"];
        if (string.IsNullOrEmpty(expected)) { return TypedResults.NotFound(); }
        var presented = context.HttpContext.Request.Headers[AspireBridgeRoutes.KeyHeader].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected))
            ? await next(context)
            : TypedResults.Unauthorized();
    }
}
