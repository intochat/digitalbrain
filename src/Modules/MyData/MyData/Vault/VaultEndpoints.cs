using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.MyData;

internal static class VaultEndpoints
{
    internal static void MapMyData(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/my-data/{owner}", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IVault>(owner).Read(HttpCaller(owner), cancellationToken)));

        endpoints.MapPost("/my-data/{owner}/fields", async (string owner, VaultFieldInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.FieldPath) || !Enum.TryParse<FieldKind>(body.Kind, ignoreCase: true, out var kind))
            {
                return Results.BadRequest();
            }

            try
            {
                var field = await grains.GetGrain<IVault>(owner).SetField(HttpCaller(owner), body.FieldPath, kind, body.Value ?? "", cancellationToken);
                return Results.Ok(field);
            }
            catch (TypeValidationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        endpoints.MapPost("/my-data/{owner}/secrets", async (string owner, VaultSecretInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.FieldPath) || string.IsNullOrWhiteSpace(body.Value))
            {
                return Results.BadRequest();
            }

            var secret = await grains.GetGrain<IVault>(owner).SetSecret(HttpCaller(owner), body.FieldPath, body.Label ?? body.FieldPath, body.Value, cancellationToken);
            return Results.Ok(secret);
        });

        endpoints.MapGet("/my-data/{owner}/export", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IVault>(owner).Export(HttpCaller(owner), cancellationToken)));

        endpoints.MapPost("/my-data/{owner}/erase", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            await grains.GetGrain<IVault>(owner).Erase(HttpCaller(owner), cancellationToken);
            return Results.Ok();
        });

        endpoints.MapGet("/my-data/{owner}/audit", async (string owner, int? limit, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IVault>(owner).Audit(HttpCaller(owner), limit ?? 50, cancellationToken)));
    }

    private static CallerContext HttpCaller(string owner) => new()
    {
        PrincipalId = owner,
        AccountId = owner,
        WorkspaceId = owner,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };
}

internal sealed record VaultFieldInput(string FieldPath, string Kind, string? Value);
internal sealed record VaultSecretInput(string FieldPath, string? Label, string? Value);
