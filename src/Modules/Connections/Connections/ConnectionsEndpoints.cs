using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Connections;

internal static class ConnectionsEndpoints
{
    internal static void MapConnections(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/connections/{owner}", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IConnections>(owner).List(cancellationToken)));

        endpoints.MapPost("/connections/{owner}/connect", async (string owner, ConnectConnectionInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            try
            {
                var record = await grains.GetGrain<IConnections>(owner).Connect(new ConnectConnection
                {
                    Source = body.Source ?? "",
                    ConnectionId = body.ConnectionId ?? "",
                    Label = body.Label,
                    Value = body.Value,
                    SecretReference = body.SecretReference,
                }, HttpCaller(owner), cancellationToken);
                return Results.Ok(record);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        endpoints.MapPost("/connections/{owner}/probe", async (string owner, ConnectionIdInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await grains.GetGrain<IConnections>(owner).Probe(body.ConnectionId ?? "", HttpCaller(owner), cancellationToken));
            }
            catch (ConnectionNotConfiguredException error)
            {
                return Results.NotFound(new { error = error.Message });
            }
        });

        endpoints.MapPost("/connections/{owner}/disconnect", async (string owner, ConnectionIdInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            await grains.GetGrain<IConnections>(owner).Disconnect(body.ConnectionId ?? "", HttpCaller(owner), cancellationToken);
            return Results.Ok();
        });
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

internal sealed record ConnectConnectionInput(string? Source, string? ConnectionId, string? Label, string? Value, string? SecretReference);
internal sealed record ConnectionIdInput(string? ConnectionId);