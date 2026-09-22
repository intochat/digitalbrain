using DigitalBrain.Behaviors;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Coding;
using DigitalBrain.Behavior;
using DigitalBrain.AI.Agents;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat;

internal static class BehaviorEndpoints
{
    public static void AddBehaviors(this IHostApplicationBuilder builder)
    {
        builder.Services.AddBehavior<ElonBitcoin>(brain =>
            [SubscriptionRequirement.For<Posted>(brain.Get<ITwitterAccount>("elonmusk"))]);
        builder.Services.AddOptions<BehaviorAuthoringOptions>().BindConfiguration("IntoChat:BehaviorAuthoring");
        builder.Services.AddSingleton<BehaviorToolService>();
        builder.Services.AddSingleton<BehaviorAuthoringService>();
        builder.Services.AddSingleton<IAgentToolFactory, BehaviorAgentTools>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext ?? throw new InvalidOperationException("Behavior MCP requires an HTTP workspace context.");
            var workspace = http.Request.RouteValues["workspaceId"]?.ToString() ?? throw new ArgumentException("Workspace is required.");
            var auth = sp.GetRequiredService<IOptions<BasicAuthOptions>>().Value;
            var scope = WorkspaceScope.Create(auth.Username is { Length: > 0 } owner ? owner : BasicAuthGate.DefaultLogin, workspace);
            return sp.GetRequiredService<BehaviorToolService>().ForScope(scope.Id);
        });
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<ScopedBehaviorTools>();
    }

    public static void MapBehaviors(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/workspaces/{workspaceId}/behaviors");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: 400); }
            catch (KeyNotFoundException error) { return Results.Problem(error.Message, statusCode: 404); }
            catch (InvalidOperationException error) { return Results.Problem(error.Message, statusCode: 409); }
            catch (InvalidDataException error) { return Results.Problem(error.Message, statusCode: 422); }
        });
        static ScopedBehaviorTools Scope(string workspaceId, BehaviorToolService service, IOptions<BasicAuthOptions> auth)
            => service.ForScope(WorkspaceScope.Create(auth.Value.Username is { Length: > 0 } owner ? owner : BasicAuthGate.DefaultLogin, workspaceId).Id);
        group.MapGet("/{id}", (string workspaceId, string id, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).ReadBehavior(id, ct));
        group.MapGet("/{id}/draft", (string workspaceId, string id, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).ReadDraft(id, ct));
        group.MapPost("/{id}/draft", (string workspaceId, string id, SaveCodeDraft request, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).SaveDraft(id, request, ct));
        group.MapPost("/{id}/checks", (string workspaceId, string id, CheckCodeDraft request, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).CheckDraft(id, request, ct));
        group.MapGet("/{id}/checks/{operationId:guid}", (string workspaceId, string id, Guid operationId, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).ReadCheck(id, operationId, ct));
        group.MapPost("/{id}/checks/{operationId:guid}/cancel", (string workspaceId, string id, Guid operationId, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).CancelCheck(id, operationId, ct));
        group.MapPost("/{id}/deploy", (string workspaceId, string id, DeployBehavior request, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).Deploy(id, request, ct));
        group.MapPost("/{id}/start", (string workspaceId, string id, ChangeBehaviorState request, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).Start(id, request, ct));
        group.MapPost("/{id}/stop", (string workspaceId, string id, ChangeBehaviorState request, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).Stop(id, request, ct));
        group.MapPost("/{id}/rollback", (string workspaceId, string id, RollbackBehavior request, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).Rollback(id, request, ct));
        group.MapGet("/{id}/logs", (string workspaceId, string id, long? after, int? limit, BehaviorToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) => Scope(workspaceId, service, auth).Logs(id, after ?? 0, limit ?? 100, ct));
        group.MapPost("/author", (string workspaceId, AuthorBehaviorRequest request, BehaviorAuthoringService service, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
            service.AuthorAsync(WorkspaceScope.Create(auth.Value.Username is { Length: > 0 } owner ? owner : BasicAuthGate.DefaultLogin, workspaceId).Id, request, ct));
        routes.MapMcp("/workspaces/{workspaceId}/behavior-mcp");
    }
}
