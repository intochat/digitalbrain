using DigitalBrain.AI.Agents;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace IntoChat;

internal static class CSharpEndpoints
{
    public static void AddCSharp(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<CSharpAuthoringOptions>().BindConfiguration("IntoChat:CSharp");
        builder.Services.AddSingleton<CSharpCatalogStore>();
        builder.Services.AddSingleton<CSharpToolService>();
        builder.Services.AddSingleton<IAgentToolFactory, CSharpAgentTools>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext ?? throw new InvalidOperationException("C# MCP requires an HTTP workspace context.");
            var workspace = http.Request.RouteValues["workspaceId"]?.ToString() ?? throw new ArgumentException("Workspace is required.");
            return sp.GetRequiredService<CSharpToolService>().ForScope(WorkspaceScope.Current(sp.GetRequiredService<IOptions<BasicAuthOptions>>().Value, workspace).Id);
        });
        // WithTools<T> constructs T itself, bypassing the workspace-scoped factory; resolve it per invocation instead.
        var tools = typeof(ScopedCSharpTools).GetMethods()
            .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length != 0)
            .Select(method => McpServerTool.Create(method, request => request.Services!.GetRequiredService<ScopedCSharpTools>()));
        builder.Services.AddMcpServer().WithHttpTransport().WithTools(tools);
    }

    public static void MapCSharp(this IEndpointRouteBuilder routes)
    {
        routes.MapMcp("/workspaces/{workspaceId}/csharp-mcp").AddEndpointFilter(async (context, next) =>
            DeveloperModeEnabled(context.HttpContext.RequestServices) ? await next(context) : Results.NotFound());
        var files = routes.MapGroup("/workspaces/{workspaceId}/csharp");
        files.AddEndpointFilter(async (context, next) =>
        {
            if (!DeveloperModeEnabled(context.HttpContext.RequestServices)) { return Results.NotFound(); }
            try { return await next(context); }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
            catch (KeyNotFoundException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound); }
            catch (InvalidOperationException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
            catch (TimeoutException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status504GatewayTimeout); }
        });
        static ScopedCSharpTools Scope(string workspaceId, CSharpToolService service, IOptions<BasicAuthOptions> auth)
            => service.ForScope(WorkspaceScope.Current(auth.Value, workspaceId).Id);
        files.MapGet("/", async (string workspaceId, CSharpToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => new { items = await Scope(workspaceId, service, auth).List(ct), allowActivation = service.AllowActivation });
        files.MapGet("/{id}", (string workspaceId, string id, CSharpToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Scope(workspaceId, service, auth).Read(id, ct));
        files.MapPut("/{id}", (string workspaceId, string id, WriteCSharpFileRequest request, CSharpToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Scope(workspaceId, service, auth).Write(id, request.Source, request.Name, request.Purpose, ct));
        files.MapPost("/{id}/start", (string workspaceId, string id, CSharpToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Scope(workspaceId, service, auth).Start(id, ct));
        files.MapPost("/{id}/stop", (string workspaceId, string id, CSharpToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Scope(workspaceId, service, auth).Stop(id, ct));
        files.MapDelete("/{id}", (string workspaceId, string id, CSharpToolService service, IOptions<BasicAuthOptions> auth, CancellationToken ct)
            => Scope(workspaceId, service, auth).Delete(id, ct));
    }

    private static bool DeveloperModeEnabled(IServiceProvider services) =>
        AgentToolPolicy.DeveloperModeEnabled(services.GetRequiredService<IConfiguration>()["IntoChat:DeveloperMode"]);
}
