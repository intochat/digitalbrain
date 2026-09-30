using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Sdk.Connectors;

internal static class ConnectorEndpoints
{
    internal static void MapConnectors(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/connections/{owner}");
        group.AddEndpointFilter(async (context, next) =>
        {
            var owner = context.HttpContext.Request.RouteValues["owner"]?.ToString();
            if (CallerContextStamper.TryGet(out var caller) && owner != caller.AccountId)
            { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            return await next(context);
        });
        group.MapGet("", async (string owner, IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await grains.GetGrain<IConnectors>(owner).List(cancellationToken)));

        group.MapPost("/connect", async (string owner, ConnectRequestInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            if (body.SecretReference is not null) { return Results.BadRequest(new { error = "Supply a connection value." }); }
            try
            {
                var record = await grains.GetGrain<IConnectors>(owner).Connect(new ConnectRequest
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

        group.MapPost("/probe", async (string owner, ConnectionIdInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await grains.GetGrain<IConnectors>(owner).Probe(body.ConnectionId ?? "", HttpCaller(owner), cancellationToken));
            }
            catch (ConnectorNotConfiguredException error)
            {
                return Results.NotFound(new { error = error.Message });
            }
        });

        group.MapPost("/disconnect", async (string owner, ConnectionIdInput body, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            await grains.GetGrain<IConnectors>(owner).Disconnect(body.ConnectionId ?? "", HttpCaller(owner), cancellationToken);
            return Results.Ok();
        });
    }

    private static CallerContext HttpCaller(string owner) => CallerContextStamper.TryGet(out var caller) ? caller : new()
    {
        PrincipalId = owner,
        AccountId = owner,
        BrainId = owner,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };
}

internal sealed record ConnectRequestInput(string? Source, string? ConnectionId, string? Label, string? Value, string? SecretReference);
internal sealed record ConnectionIdInput(string? ConnectionId);
