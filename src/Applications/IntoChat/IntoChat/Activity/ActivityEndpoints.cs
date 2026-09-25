using System.Text.Json;
using DigitalBrain.Core;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Activity;

public static class ActivityEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ScopeId(string owner, string workspaceId) => WorkspaceScope.Create(owner, workspaceId).Id;

    public static ActivitySnapshot ReadSnapshot(ActivityFeed feed, string owner, string workspaceId, long? after = null)
        => feed.Snapshot(ScopeId(owner, workspaceId), after);

    public static void MapNeuronActivity(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/workspaces/{workspaceId}/activity", (string workspaceId, ActivityFeed feed,
            IOptions<BasicAuthOptions> auth, long? after) =>
        {
            try { return Results.Ok(ReadSnapshot(feed, Owner(auth.Value), workspaceId, after)); }
            catch (ArgumentException) { return Results.BadRequest(); }
        });

        routes.MapGet("/workspaces/{workspaceId}/activity/events", async (string workspaceId, long? after, Guid? generation,
            ActivityFeed feed, IOptions<BasicAuthOptions> auth, HttpContext http) =>
        {
            string scope;
            try { scope = ScopeId(Owner(auth.Value), workspaceId); }
            catch (ArgumentException) { http.Response.StatusCode = 400; return; }

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            var cursor = after ?? feed.Snapshot(scope).NextSequence;
            if (generation.HasValue && generation.Value != feed.Generation)
            {
                var fresh = feed.Snapshot(scope);
                await Emit(http, "gap", fresh, null);
                cursor = fresh.NextSequence;
            }
            await foreach (var update in feed.Watch(scope, cursor, http.RequestAborted))
            {
                if (update.Gap)
                {
                    await Emit(http, "gap", feed.Snapshot(scope), null);
                }
                else if (update.Event is { } item)
                {
                    await Emit(http, "activity", item, item.Sequence);
                }
            }
        });
    }

    private static string Owner(BasicAuthOptions auth)
        => auth.Username is { Length: > 0 } username ? username : BasicAuthGate.DefaultLogin;

    private static async Task Emit(HttpContext http, string kind, object item, long? id)
    {
        var frame = $"event: {kind}\n" + (id.HasValue ? $"id: {id.Value}\n" : "")
            + "data: " + JsonSerializer.Serialize(item, Json) + "\n\n";
        await http.Response.WriteAsync(frame, http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
    }
}
