using System.Net.WebSockets;
using System.Text;
using Brain.Client;
using Brain.Contracts;
using Brain.UiGateway;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddBrainClient();
var app = builder.Build();
app.MapDefaultEndpoints();
app.UseWebSockets();

app.MapPost("/ui/invoke", InvokePost);
app.MapGet("/ui/read", ReadGet);
app.MapGet("/ui/describe", DescribeGet);
app.Map("/ui/watch", WatchAsync);

app.Run();

async Task<IResult> InvokePost(UiInvokeRequest request, IClusterClient client)
{
    try
    {
        var receipt = await UiEndpoints.InvokeAsync(
            client, UiEndpoints.DevCallerKey, request.Address, request.Contract, request.InputJson, request.CommandId, request.ExpectedRevision);
        return Results.Ok(receipt);
    }
    catch (BrainException exception)
    {
        return Results.Conflict(UiEndpoints.ToErrorPayload(exception));
    }
}

async Task<IResult> ReadGet(string address, string projection, IClusterClient client)
{
    try
    {
        return Results.Ok(await UiEndpoints.ReadAsync(client, address, projection));
    }
    catch (BrainException exception)
    {
        return Results.Conflict(UiEndpoints.ToErrorPayload(exception));
    }
}

async Task<IResult> DescribeGet(string address, IClusterClient client)
{
    try
    {
        return Results.Ok(await UiEndpoints.DescribeAsync(client, address));
    }
    catch (BrainException exception)
    {
        return Results.Conflict(UiEndpoints.ToErrorPayload(exception));
    }
}

async Task WatchAsync(HttpContext context)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var client = context.RequestServices.GetRequiredService<IClusterClient>();
    var cursor = long.TryParse(context.Request.Query["cursor"], out var parsedCursor) ? parsedCursor : 0;
    var space = context.Request.Query["space"].FirstOrDefault() ?? "actor/ui-dev";
    var address = new NeuronAddress("local-owner", space, "feed/main").ToGrainKey();

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

static Task SendTextAsync(WebSocket socket, string text, CancellationToken cancellationToken) =>
    socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

internal sealed record UiInvokeRequest(string Address, string Contract, string InputJson, string CommandId, long? ExpectedRevision);
