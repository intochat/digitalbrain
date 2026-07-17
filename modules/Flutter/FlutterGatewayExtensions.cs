using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Brain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Brain.Modules.Flutter;

public static class FlutterGatewayExtensions
{
    public static WebApplication MapDigitalBrainFlutter(this WebApplication app)
    {
        app.UseWebSockets();

        app.MapPost("/ui/invoke", InvokePost);
        app.MapGet("/ui/read", ReadGet);
        app.MapGet("/ui/describe", DescribeGet);
        app.Map("/ui/watch", WatchAsync);
        return app;
    }

    private static async Task<IResult> InvokePost(
        UiInvokeRequest request,
        ClaimsPrincipal principal,
        IClusterClient client,
        FlutterGatewayPolicy policy)
    {
        try
        {
            return Results.Ok(await UiEndpoints.InvokeAsync(
                client,
                principal,
                policy,
                request.Address,
                request.Contract,
                request.InputJson,
                request.CommandId,
                request.ExpectedRevision));
        }
        catch (BrainException exception)
        {
            return Error(exception);
        }
    }

    private static async Task<IResult> ReadGet(
        string address,
        string projection,
        ClaimsPrincipal principal,
        IClusterClient client,
        FlutterGatewayPolicy policy)
    {
        try
        {
            return Results.Ok(await UiEndpoints.ReadAsync(client, principal, policy, address, projection));
        }
        catch (BrainException exception)
        {
            return Error(exception);
        }
    }

    private static async Task<IResult> DescribeGet(
        string address,
        ClaimsPrincipal principal,
        IClusterClient client,
        FlutterGatewayPolicy policy)
    {
        try
        {
            return Results.Ok(await UiEndpoints.DescribeAsync(client, principal, policy, address));
        }
        catch (BrainException exception)
        {
            return Error(exception);
        }
    }

    private static async Task WatchAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        FlutterGatewaySession session;
        try
        {
            session = FlutterGatewaySession.FromPrincipal(context.User);
        }
        catch (BrainException exception)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(UiEndpoints.ToErrorPayload(exception));
            return;
        }

        var client = context.RequestServices.GetRequiredService<IClusterClient>();
        var cursor = long.TryParse(context.Request.Query["cursor"], out var parsedCursor) ? parsedCursor : 0;
        var address = new NeuronAddress(session.OwnerId, session.SpaceId, "feed/main").ToGrainKey();

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var lastSentAt = DateTimeOffset.UtcNow;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var page = await client.GetGrain<INeuron>(address).ReadEventsAsync(cursor, 100);
                var frames = WatchPager.NextFrames(page);
                cursor = WatchPager.NextCursor(page);

                if (frames.Count == 0)
                {
                    if (DateTimeOffset.UtcNow - lastSentAt >= TimeSpan.FromSeconds(15))
                    {
                        await SendTextAsync(socket, "{\"ping\":true}", context.RequestAborted);
                        lastSentAt = DateTimeOffset.UtcNow;
                    }
                    await Task.Delay(700, context.RequestAborted);
                    continue;
                }

                foreach (var frame in frames)
                    await SendTextAsync(socket, frame, context.RequestAborted);
                lastSentAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
    }

    private static IResult Error(BrainException exception) =>
        exception.Code switch
        {
            "auth.required" => Results.Json(UiEndpoints.ToErrorPayload(exception), statusCode: StatusCodes.Status401Unauthorized),
            BrainErrors.GrantDenied or BrainErrors.GrantMissing =>
                Results.Json(UiEndpoints.ToErrorPayload(exception), statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Conflict(UiEndpoints.ToErrorPayload(exception))
        };

    private static Task SendTextAsync(WebSocket socket, string text, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    private sealed record UiInvokeRequest(
        string Address,
        string Contract,
        string InputJson,
        string CommandId,
        long? ExpectedRevision);
}
