using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter.Workspace.Signals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter.Workspace;

internal static class WorkspaceEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var brain = BrainRoutes.Group(endpoints);
        brain.MapGet("", (IDigitalBrain digitalBrain, CancellationToken ct)
            => Respond(async () => Results.Ok(await Workspace(digitalBrain).Read().WaitAsync(ct))));
        brain.MapPost("/windows/{windowId}/close", (string windowId, CloseWindow input, IDigitalBrain digitalBrain, CancellationToken ct)
            => Respond(async () => Results.Ok(await Workspace(digitalBrain).Close(windowId, input.ExpectedRevision).WaitAsync(ct))));
        brain.MapPost("/windows/{windowId}/reopen", (string windowId, ReopenWindow input, IDigitalBrain digitalBrain, CancellationToken ct)
            => Respond(async () =>
            {
                var workspace = Workspace(digitalBrain);
                var state = await workspace.Read().WaitAsync(ct);
                var window = state.Windows.SingleOrDefault(w => w.Id == windowId) ?? throw new KeyNotFoundException("Window not found.");
                return Results.Ok(window.Reference.Kind == WindowReference.TableKind
                    ? (await workspace.Open(new(input.OperationId, window.Id, window.Title, window.Reference, input.ExpectedRevision)).WaitAsync(ct)).State
                    : await workspace.OpenSurface(new(input.OperationId, window.Id, window.Title, window.Reference, input.ExpectedRevision)).WaitAsync(ct));
            }));
        brain.MapPost("/connected-sources", (SetConnectedSources input, IDigitalBrain digitalBrain, CancellationToken ct)
            => Respond(async () => Results.Ok(await Workspace(digitalBrain).SetConnectedSources(input.Sources ?? []).WaitAsync(ct))));
        brain.MapGet("/events", Events);
        brain.MapPost("/reports", (ProblemReportInput input, IDigitalBrain digitalBrain, CancellationToken ct) => ProblemReportEndpoint.File(input, digitalBrain, ct));
    }

    private static async Task Events(HttpContext http, IDigitalBrain digitalBrain, CancellationToken ct)
    {
        var workspace = Workspace(digitalBrain);
        await using var changes = await digitalBrain.SubscribeAsync<WorkspaceChanged>(workspace, ct);
        var initial = await workspace.Read().WaitAsync(ct);
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        var brainId = CallerContextStamper.Require().BrainId;
        await Write(initial.Revision);
        await foreach (var change in changes.ReadAllAsync(ct)) { await Write(change.Revision); }
        async Task Write(long revision)
        {
            await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(new { brainId, revision }, Json) + "\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }
    }

    private static IWorkspace Workspace(IDigitalBrain digitalBrain) => digitalBrain.Get<IWorkspace>(BrainScope.CurrentId());

    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (WorkspaceRevisionConflictException error) { return Results.Conflict(new { error = error.Message }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "The requested workspace item was not found." }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (TimeoutException) { return Results.Problem("The workspace operation timed out. Refresh before retrying.", statusCode: 503); }
    }

    internal sealed record CloseWindow(long ExpectedRevision);
    internal sealed record ReopenWindow(string OperationId, long ExpectedRevision);
    internal sealed record SetConnectedSources(IReadOnlyList<string>? Sources);
}
