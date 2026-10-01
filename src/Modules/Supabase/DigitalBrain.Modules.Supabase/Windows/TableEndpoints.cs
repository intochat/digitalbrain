using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Supabase.Windows;

internal static class TableEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var tables = BrainRoutes.Group(endpoints, "/tables");
        tables.MapGet("/{tableId}", (string tableId, int? offset, int? limit, IDigitalBrain brain, CancellationToken ct)
            => Respond(async () =>
            {
                var table = await ResolveTable(brain, tableId, ct);
                var snapshot = await table.Read(new(offset ?? 0, limit ?? 25)).WaitAsync(ct) ?? throw new KeyNotFoundException("Table not found.");
                return Results.Ok(LiveTableJson.ToJson(snapshot));
            }));
        tables.MapPost("/{tableId}/view", (string tableId, LiveTableView input, IDigitalBrain brain, CancellationToken ct)
            => Respond(async () =>
            {
                var table = await ResolveTable(brain, tableId, ct);
                await table.UpdateView(input.ToRequest()).WaitAsync(ct);
                var snapshot = await table.Read(new(0, 25)).WaitAsync(ct) ?? throw new KeyNotFoundException("Table not found.");
                return Results.Ok(LiveTableJson.ToJson(snapshot));
            }));
    }

    // A table is reachable only through a window the brain's workspace has open on it.
    private static async Task<ISupabaseTable> ResolveTable(IDigitalBrain brain, string tableId, CancellationToken ct)
    {
        var state = await brain.Get<IWorkspace>(BrainScope.CurrentId()).Read().WaitAsync(ct);
        if (!state.Windows.Any(w => w.Reference.Kind == WindowReference.TableKind && w.Reference.NeuronId == tableId)) { throw new KeyNotFoundException("Table not found in this workspace."); }
        return brain.Get<ISupabaseTable>(tableId);
    }

    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SupabaseTableRevisionConflictException error) { return Results.Conflict(new { error = error.Message }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested workspace item was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (SupabaseTableSourceException) { return Results.Problem("The table source is unavailable.", statusCode: 503); }
        catch (TimeoutException) { return Results.Problem("The workspace operation timed out. Refresh before retrying.", statusCode: 503); }
    }
}
