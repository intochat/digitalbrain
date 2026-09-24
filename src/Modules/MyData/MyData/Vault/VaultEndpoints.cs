using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.MyData;

internal static class VaultEndpoints
{
    internal static void MapMyData(this IEndpointRouteBuilder endpoints)
    {
        var vault = endpoints.MapGroup("/my-data/{owner}").AddEndpointFilter(OnlyTheOwner);

        vault.MapGet("", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IVault>(owner).Read(CallerContextStamper.Require(), cancellationToken)));

        vault.MapPost("/fields", async (string owner, VaultFieldInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.FieldPath) || !Enum.TryParse<FieldKind>(body.Kind, ignoreCase: true, out var kind))
            {
                return Results.BadRequest();
            }

            try
            {
                var field = await grains.GetGrain<IVault>(owner).SetField(CallerContextStamper.Require(), body.FieldPath, kind, body.Value ?? "", cancellationToken);
                return Results.Ok(field);
            }
            catch (TypeValidationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        vault.MapPost("/secrets", async (string owner, VaultSecretInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.FieldPath) || string.IsNullOrWhiteSpace(body.Value))
            {
                return Results.BadRequest();
            }

            var secret = await grains.GetGrain<IVault>(owner).SetSecret(CallerContextStamper.Require(), body.FieldPath, body.Label ?? body.FieldPath, body.Value, cancellationToken);
            return Results.Ok(secret);
        });

        vault.MapGet("/export", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IVault>(owner).Export(CallerContextStamper.Require(), cancellationToken)));

        vault.MapPost("/erase", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            await grains.GetGrain<IVault>(owner).Erase(CallerContextStamper.Require(), cancellationToken);
            return Results.Ok();
        });

        vault.MapGet("/audit", async (string owner, int? limit, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IVault>(owner).Audit(CallerContextStamper.Require(), limit ?? 50, cancellationToken)));
    }

    // A vault is reachable only by its own principal, as stamped at the authenticated edge; the
    // owner segment in the path is an address, never a credential.
    private static async ValueTask<object?> OnlyTheOwner(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!CallerContextStamper.TryGet(out var caller) || caller is null)
        {
            return Results.Unauthorized();
        }

        var owner = context.HttpContext.Request.RouteValues["owner"] as string;
        if (!string.Equals(owner, caller.PrincipalId, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }
}

internal sealed record VaultFieldInput(string FieldPath, string Kind, string? Value);
internal sealed record VaultSecretInput(string FieldPath, string? Label, string? Value);
