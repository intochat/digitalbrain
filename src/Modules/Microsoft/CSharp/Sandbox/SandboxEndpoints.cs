namespace DigitalBrain.Microsoft.CSharp.Sandbox;

internal static class SandboxEndpoints
{
    public static void MapSandbox(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => TypedResults.Ok());
        endpoints.MapPost("/runs/{identifier}", async (string identifier, RunRequest request, SandboxRuns runs, CancellationToken cancellationToken) =>
        {
            try { return Results.Accepted($"/runs/{identifier}", await runs.StartAsync(identifier, request, cancellationToken)); }
            catch (ArgumentException error) { return Results.BadRequest(error.Message); }
            catch (InvalidOperationException error) { return Results.Conflict(error.Message); }
        });
        endpoints.MapGet("/runs/{identifier}", (string identifier, SandboxRuns runs)
            => runs.Find(identifier) is { } status ? Results.Ok(status) : Results.NotFound());
        endpoints.MapGet("/runs/{identifier}/logs", (string identifier, int? tail, SandboxRuns runs)
            => runs.ReadLogs(identifier, tail ?? 200) is { } logs ? Results.Text(logs) : Results.NotFound());
        endpoints.MapPost("/runs/{identifier}/stop", async (string identifier, SandboxRuns runs)
            => await runs.StopAsync(identifier) is { } status ? Results.Ok(status) : Results.NotFound());
    }
}
