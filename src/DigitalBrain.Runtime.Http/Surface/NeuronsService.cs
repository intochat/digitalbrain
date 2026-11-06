using System.Text.Json;
using DigitalBrain.Runtime.Http.Surface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Runtime.Http.Surface;

/// <summary>
/// Exposes HTTP endpoint surface for dispatching neuron calls.
/// </summary>
public static class NeuronsService
{
    public sealed record NeuronCallRequest(string Contract, string Key, string Method, JsonElement[]? Arguments);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = BrainRoutes.Group(endpoints, "/neurons");
        group.MapPost("/call", Call);
    }

    private static async Task<IResult> Call(HttpContext http, string brainId, NeuronCallRequest request, NeuronSurface surface, ILogger<NeuronSurface> logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var args = request.Arguments is null ? "[]" : JsonSerializer.Serialize(request.Arguments);
            var result = await surface.Call(brainId, request.Contract, request.Key, request.Method, args, http.RequestAborted);
            return result is { } json ? Results.Json(json) : Results.NoContent();
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (KeyNotFoundException error)
        {
            return Results.NotFound(new { error = error.Message });
        }
        catch (Exception error)
        {
            LogCallFailed(logger, error);
            return Results.Problem("The neuron call failed.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static readonly Action<ILogger, Exception?> LogCallFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(1, nameof(Call)), "Neuron call failed");
}
