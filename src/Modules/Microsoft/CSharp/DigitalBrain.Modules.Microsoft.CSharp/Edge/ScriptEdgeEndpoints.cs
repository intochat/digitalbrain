using System.Net.ServerSentEvents;
using DigitalBrain.Client;
using DigitalBrain.Contracts.Edge.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Microsoft.CSharp;

internal static class ScriptEdgeEndpoints
{
    public static void MapScriptEdge(this IEndpointRouteBuilder endpoints)
    {
        var edge = endpoints.MapGroup("/").ExcludeFromDescription();
        edge.MapPost(ScriptEdgeProtocol.Invoke, async Task<IResult> (HttpContext http, ScriptInvocation invocation, ScriptEdge scripts, CancellationToken cancellationToken) =>
        {
            try
            {
                return await scripts.InvokeAsync(Bearer(http), (invocation.Contract, invocation.Key, invocation.Method, invocation.Arguments), cancellationToken) is { } value ? Results.Json(value) : Results.NoContent();
            }
            catch (Exception error) { return Refusal(http, error); }
        });
        edge.MapGet(ScriptEdgeProtocol.Signals, async Task<IResult> (HttpContext http, string contract, string key, string signal, ScriptEdge scripts, CancellationToken cancellationToken) =>
        {
            try { return TypedResults.ServerSentEvents(Events(await scripts.OpenSignalsAsync(Bearer(http), contract, key, signal, cancellationToken))); }
            catch (Exception error) { return Refusal(http, error); }
        });
    }

    private static async IAsyncEnumerable<SseItem<string>> Events(IAsyncEnumerable<string> signals)
    {
        yield return new SseItem<string>("", ScriptEdgeProtocol.ReadyEvent);
        await foreach (var signal in signals) { yield return new SseItem<string>(signal, ScriptEdgeProtocol.SignalEvent); }
    }

    private static string? Bearer(HttpContext http)
        => http.Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : null;

    private static IResult Refusal(HttpContext http, Exception error)
    {
        if (error is DigitalBrain.Apps.AppRequirementsException or DigitalBrain.Sdk.Capacity.CapacityUnavailableException)
        {
            http.Response.Headers[ScriptEdgeProtocol.RefusalHeader] = error.GetType().Name;
            return Results.Text(error.Message, statusCode: error is DigitalBrain.Apps.AppRequirementsException ? 409 : 503);
        }
        return error switch
        {
            UnauthorizedAccessException => Results.Text(error.Message, statusCode: StatusCodes.Status401Unauthorized),
            ArgumentException or NotSupportedException or System.Text.Json.JsonException => Results.Text(error.Message, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Text(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }
}
