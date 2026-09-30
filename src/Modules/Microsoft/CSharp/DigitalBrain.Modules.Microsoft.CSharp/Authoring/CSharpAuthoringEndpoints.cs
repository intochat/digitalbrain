using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.CSharp;

internal static class CSharpAuthoringEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var files = BrainRoutes.Group(endpoints, "/csharp");
        files.AddEndpointFilter(async (context, next) =>
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            if (!CSharpAuthoringGate.IsOpen(configuration)) { return Results.NotFound(); }
            try { return await next(context); }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
            catch (UnauthorizedAccessException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden); }
            catch (KeyNotFoundException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound); }
            catch (InvalidOperationException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
            catch (InvalidDataException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
            catch (TimeoutException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status504GatewayTimeout); }
        });
        files.MapGet("", async (ScopedCSharpTools tools, CancellationToken ct) => new { items = await tools.List(ct), canRun = tools.CanRun });
        files.MapGet("/{id}", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Read(id, ct));
        files.MapPut("/{id}", (string id, WriteCSharpFileRequest request, ScopedCSharpTools tools, CancellationToken ct)
            => tools.Write(id, request.Source, request.Name, request.Purpose, ct));
        files.MapPost("/{id}/start", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Start(id, ct));
        files.MapPost("/{id}/stop", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Stop(id, ct));
        files.MapDelete("/{id}", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Delete(id, ct));
        files.MapPost("/{id}/share", (string id, ShareCSharpRequest request, CSharpSharing sharing, CancellationToken ct)
            => sharing.Share(id, request, ct));
    }
}
