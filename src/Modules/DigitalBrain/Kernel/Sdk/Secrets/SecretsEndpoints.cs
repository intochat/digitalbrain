using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Sdk.Secrets;

internal static class SecretsEndpoints
{
    internal static void MapSecrets(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/secrets/{owner}", async (string owner, SecretInput body, IGrainFactory grains) =>
        {
            if (!CallerContextStamper.TryGet(out var caller) || caller is null)
            {
                return Results.Unauthorized();
            }

            if (caller.PrincipalId != owner)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Value))
            {
                return Results.BadRequest();
            }

            var secret = await grains.GetGrain<ISecrets>(owner).Set(caller, body.Name, body.Label ?? body.Name, body.Value);
            return Results.Ok(secret);
        });
    }
}

internal sealed record SecretInput(string Name, string? Label, string? Value);
