using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Flutter.Workspace.Signals;
using DigitalBrain.Supabase.Tables;
using Microsoft.Extensions.Options;

namespace IntoChat.Workspace;

internal static class WorkspaceEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapWorkspaceDataEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/workspaces/{workspaceId}", (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Respond(async () => Results.Ok(await GetWorkspace(brain, auth.Value, workspaceId).Read().WaitAsync(ct))));
        routes.MapPost("/workspaces/{workspaceId}/windows/{windowId}/close",
            (string workspaceId, string windowId, CloseWindow input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Respond(async () => Results.Ok(await GetWorkspace(brain, auth.Value, workspaceId).Close(windowId, input.ExpectedRevision).WaitAsync(ct))));
        routes.MapPost("/workspaces/{workspaceId}/windows/{windowId}/reopen",
            (string workspaceId, string windowId, ReopenWindow input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Respond(async () =>
            {
                var workspace = GetWorkspace(brain, auth.Value, workspaceId);
                var state = await workspace.Read().WaitAsync(ct);
                var window = state.Windows.SingleOrDefault(w => w.Id == windowId) ?? throw new KeyNotFoundException("Window not found.");
                return Results.Ok(window.Reference.Kind == WindowReference.TableKind
                    ? (await workspace.Open(new(input.OperationId, window.Id, window.Title, window.Reference, input.ExpectedRevision)).WaitAsync(ct)).State
                    : await workspace.OpenSurface(new(input.OperationId, window.Id, window.Title, window.Reference, input.ExpectedRevision)).WaitAsync(ct));
            }));
        routes.MapGet("/workspaces/{workspaceId}/tables/{tableId}",
            (string workspaceId, string tableId, int? offset, int? limit, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Respond(async () =>
            {
                var table = await ResolveTable(brain, auth.Value, workspaceId, tableId, ct);
                var snapshot = await table.Read(new(offset ?? 0, limit ?? 25)).WaitAsync(ct) ?? throw new KeyNotFoundException("Table not found.");
                return Results.Ok(WorkspaceTableAdapter.ToJson(snapshot));
            }));
        routes.MapPost("/workspaces/{workspaceId}/tables/{tableId}/view",
            (string workspaceId, string tableId, WorkspaceTableView input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Respond(async () =>
            {
                var table = await ResolveTable(brain, auth.Value, workspaceId, tableId, ct);
                await table.UpdateView(input.ToRequest()).WaitAsync(ct);
                var snapshot = await table.Read(new(0, 25)).WaitAsync(ct) ?? throw new KeyNotFoundException("Table not found.");
                return Results.Ok(WorkspaceTableAdapter.ToJson(snapshot));
            }));
        routes.MapGet("/workspaces/{workspaceId}/events", Events);
    }

    private static async Task Events(string workspaceId, HttpContext http, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct)
    {
        IWorkspace workspace;
        try { workspace = GetWorkspace(brain, auth.Value, workspaceId); }
        catch (ArgumentException) { http.Response.StatusCode = 400; return; }
        await using var changes = await brain.SubscribeAsync<WorkspaceChanged>(workspace, ct);
        var initial = await workspace.Read().WaitAsync(ct);
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        await Write(initial.Revision);
        await foreach (var change in changes.ReadAllAsync(ct)) { await Write(change.Revision); }
        async Task Write(long revision)
        {
            await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(new { workspaceId, revision }, Json) + "\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }
    }

    internal static IWorkspace GetWorkspace(IDigitalBrain brain, BasicAuthOptions auth, string workspaceId)
        => brain.Get<IWorkspace>(WorkspaceScope.Create(auth.Username is { Length: > 0 } owner ? owner : AccountSession.DefaultLogin, workspaceId).Id);

    private static async Task<ISupabaseTable> ResolveTable(IDigitalBrain brain, BasicAuthOptions auth, string workspaceId, string tableId, CancellationToken ct)
    {
        var state = await GetWorkspace(brain, auth, workspaceId).Read().WaitAsync(ct);
        if (!state.Windows.Any(w => w.Reference.Kind == WindowReference.TableKind && w.Reference.NeuronId == tableId)) { throw new KeyNotFoundException("Table not found in this workspace."); }
        return brain.Get<ISupabaseTable>(tableId);
    }
    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (WorkspaceRevisionConflictException error) { return Results.Conflict(new { error = error.Message }); }
        catch (SupabaseTableRevisionConflictException error) { return Results.Conflict(new { error = error.Message }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested workspace item was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (SupabaseTableSourceException) { return Results.Problem("The table source is unavailable.", statusCode: 503); }
        catch (TimeoutException) { return Results.Problem("The workspace operation timed out. Refresh before retrying.", statusCode: 503); }
    }
    internal sealed record CloseWindow(long ExpectedRevision);
    internal sealed record ReopenWindow(string OperationId, long ExpectedRevision);
}