using System.Text.Json;
using DigitalBrain.Abstractions.Programming;
using DigitalBrain.Core.Programming;

namespace IntoChat;

internal static class ProgramEndpoints
{
    public static void AddProgramming(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ProgramCompiler>();
        builder.Services.AddSingleton<ProgramCodeRunner>();
        builder.Services.AddSingleton<ProgramLiveEvents>();
        builder.Services.AddSingleton<ProgramAgents>();
        builder.Services.AddSingleton<IProgramRunLifecycle>(services => services.GetRequiredService<ProgramAgents>());
        builder.Services.AddSingleton<IProgramRunCancellation>(services => services.GetRequiredService<ProgramLiveEvents>());
        builder.Services.AddSingleton<ProgramTools>();
        builder.Services.AddSingleton<ProgramAgentTools>();
        builder.Services.AddSingleton<IProgramNodeExecutor, IntoChatProgramExecutor>();
    }

    public static void MapProgramming(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/programs");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (TimeoutException error)
            {
                return Results.Json(new { error = error.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        group.MapGet("/", (ProgramService programs, CancellationToken ct) => programs.ListAsync(ct));
        group.MapGet("/examples", () => ProgramExamples.All);
        group.MapGet("/agent-models", (DigitalBrain.AI.ModelProfiles models) => models.List());
        group.MapPost("/compile", (CompileProgramRequest request, ProgramCompiler compiler, CancellationToken ct)
            => compiler.CompileAsync(request.Intent, ct));
        group.MapPost("/validate", (ValidateProgramRequest request, ProgramService programs) => programs.Validate(request.Definition));
        group.MapGet("/{id}", (string id, ProgramService programs, CancellationToken ct) => programs.ReadAsync(id, ct));
        group.MapPut("/{id}", (string id, DeployProgramRequest request, ProgramService programs, CancellationToken ct) =>
        {
            if (request.Definition.Id != id) { throw new ArgumentException("Definition id must match the URL."); }
            return programs.DeployAsync(request.Definition, request.ExpectedVersion, ct);
        });
        group.MapPost("/{id}/run", (string id, RunProgramRequest request, ProgramService programs, CancellationToken ct)
            => programs.RunAsync(id, request.Input, request.RunId, ct));
        group.MapGet("/{id}/runs/{runId}", (string id, string runId, ProgramService programs, CancellationToken ct)
            => programs.ReadRunAsync(id, runId, ct));
        group.MapPost("/{id}/runs/{runId}/cancel", async (string id, string runId, ProgramService programs, CancellationToken ct) =>
        {
            await programs.CancelAsync(id, runId, ct);
            return Results.Ok(new { status = "Cancellation requested" });
        });
        group.MapPost("/{id}/enabled", (string id, EnableProgramRequest request, ProgramService programs, CancellationToken ct)
            => programs.SetEnabledAsync(id, request.Enabled, request.ExpectedVersion, ct));
        group.MapPost("/{id}/rollback", (string id, RollbackProgramRequest request, ProgramService programs, CancellationToken ct)
            => programs.RollbackAsync(id, request.Version, request.ExpectedVersion, ct));
    }
}

internal sealed record CompileProgramRequest(string Intent);
internal sealed record ValidateProgramRequest(ProgramDefinition Definition);
internal sealed record DeployProgramRequest(ProgramDefinition Definition, long? ExpectedVersion = null);
internal sealed record RunProgramRequest(JsonElement Input, string? RunId = null);
internal sealed record EnableProgramRequest(bool Enabled, long? ExpectedVersion = null);
internal sealed record RollbackProgramRequest(long Version, long? ExpectedVersion = null);
