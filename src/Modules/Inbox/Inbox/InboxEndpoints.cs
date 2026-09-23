using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Contracts;
using DigitalBrain.Inbox.Signals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans;

namespace DigitalBrain.Inbox;

internal static class InboxEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static void MapInbox(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/inbox/{workspaceId}", static async (string workspaceId, IGrainFactory grains, CancellationToken ct) =>
            Results.Ok(await Inbox(grains, workspaceId).Read().WaitAsync(ct)));

        endpoints.MapPost("/inbox/{workspaceId}/items/{itemId}/read", static async (string workspaceId, string itemId, IGrainFactory grains, CancellationToken ct) =>
        {
            await Inbox(grains, workspaceId).MarkRead(itemId).WaitAsync(ct);
            return Results.NoContent();
        });

        endpoints.MapPost("/inbox/{workspaceId}/items/{itemId}/resolve", static async (string workspaceId, string itemId, IGrainFactory grains, CancellationToken ct) =>
        {
            await Inbox(grains, workspaceId).Resolve(itemId).WaitAsync(ct);
            return Results.NoContent();
        });

        endpoints.MapPost("/inbox/{workspaceId}/digest", static async (string workspaceId, InboxDigestRequest request, IGrainFactory grains, CancellationToken ct) =>
            Results.Ok(new InboxDigestResult(await Inbox(grains, workspaceId)
                .DigestApprovals(request.MinimumWait, request.Recipient).WaitAsync(ct))));

        endpoints.MapGet("/inbox/{workspaceId}/events", Events);
    }

    private static IInboxFeed Inbox(IGrainFactory grains, string workspaceId) => grains.GetGrain<IInboxFeed>(workspaceId);

    private static async Task Events(string workspaceId, HttpContext http, IDigitalBrain brain, CancellationToken ct)
    {
        var inbox = brain.Get<IInboxFeed>(workspaceId);
        await using var changes = await brain.SubscribeAsync<InboxChanged>(inbox, ct);
        var snapshot = await inbox.Read().WaitAsync(ct);
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        await Write(http, "snapshot", snapshot, ct);
        await foreach (var change in changes.ReadAllAsync(ct)) { await Write(http, "item", change, ct); }

        static async Task Write<T>(HttpContext http, string eventName, T payload, CancellationToken ct)
        {
            await http.Response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, Json)}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }
    }
}

public sealed record InboxDigestRequest(TimeSpan MinimumWait, string Recipient);

public sealed record InboxDigestResult(int Digested);
