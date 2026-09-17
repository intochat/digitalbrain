using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Core.Behavior;

namespace IntoChat;

internal static class BehaviorEndpoints
{
    public static void AddBehaviors(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<BehaviorCompiler>();
        builder.Services.AddSingleton<BehaviorCodeRunner>();
        builder.Services.AddSingleton<BehaviorLiveEvents>();
        builder.Services.AddSingleton<BehaviorAgents>();
        builder.Services.AddSingleton<IBehaviorRunLifecycle>(services => services.GetRequiredService<BehaviorAgents>());
        builder.Services.AddSingleton<IBehaviorRunCancellation>(services => services.GetRequiredService<BehaviorLiveEvents>());
        builder.Services.AddSingleton<BehaviorTools>();
        builder.Services.AddSingleton<BehaviorAgentTools>();
        builder.Services.AddSingleton<IBehaviorNodeExecutor, IntoChatBehaviorExecutor>();
        builder.Services.AddSingleton<IBehaviorDefaults, IntoChatBehaviorDefaults>();
    }

    public static void MapBehaviors(this IEndpointRouteBuilder endpoints)
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
        group.MapGet("/", (BehaviorService programs, CancellationToken ct) => programs.ListAsync(ct));
        group.MapGet("/examples", () => BehaviorExamples.All);
        group.MapGet("/agent-models", (DigitalBrain.AI.ModelProfiles models) => models.List());
        group.MapPost("/compile", (CompileBehaviorRequest request, BehaviorCompiler compiler, CancellationToken ct)
            => compiler.CompileAsync(request.Intent, ct));
        group.MapPost("/validate", (ValidateBehaviorRequest request, BehaviorService programs) => programs.Validate(request.Definition));
        group.MapGet("/{id}", (string id, BehaviorService programs, CancellationToken ct) => programs.ReadAsync(id, ct));
        group.MapPut("/{id}", (string id, DeployBehaviorRequest request, BehaviorService programs, CancellationToken ct) =>
        {
            if (request.Definition.Id != id) { throw new ArgumentException("Definition id must match the URL."); }
            return programs.DeployAsync(request.Definition, request.ExpectedVersion, ct);
        });
        group.MapPost("/{id}/run", (string id, RunBehaviorRequest request, BehaviorService programs, CancellationToken ct)
            => programs.RunAsync(id, request.Input, request.RunId, ct));
        group.MapGet("/{id}/runs/{runId}", (string id, string runId, BehaviorService programs, CancellationToken ct)
            => programs.ReadRunAsync(id, runId, ct));
        group.MapPost("/{id}/runs/{runId}/cancel", async (string id, string runId, BehaviorService programs, CancellationToken ct) =>
        {
            await programs.CancelAsync(id, runId, ct);
            return Results.Ok(new { status = "Cancellation requested" });
        });
        group.MapPost("/{id}/enabled", (string id, EnableBehaviorRequest request, BehaviorService programs, CancellationToken ct)
            => programs.SetEnabledAsync(id, request.Enabled, request.ExpectedVersion, ct));
        group.MapPost("/{id}/rollback", (string id, RollbackBehaviorRequest request, BehaviorService programs, CancellationToken ct)
            => programs.RollbackAsync(id, request.Version, request.ExpectedVersion, ct));
    }
}
