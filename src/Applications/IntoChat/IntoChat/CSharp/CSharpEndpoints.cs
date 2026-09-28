using DigitalBrain.Core.Enforcement;
using DigitalBrain.AI.Agents;
using DigitalBrain.Microsoft.CSharp;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using DigitalBrain.Identity;

namespace IntoChat;

internal static class CSharpEndpoints
{
    public static void AddCSharp(this IHostApplicationBuilder builder)
    {
        builder.Services.AddCSharpAuthoring();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext ?? throw new InvalidOperationException("C# tools require an HTTP workspace context.");
            var workspace = http.Request.RouteValues["workspaceId"]?.ToString() ?? throw new ArgumentException("Workspace is required.");
            return sp.GetRequiredService<CSharpToolService>().ForScope(WorkspaceScope.Current(sp.GetRequiredService<IOptions<BasicAuthOptions>>().Value, workspace).Id);
        });
        // WithTools<T> constructs T itself, bypassing the workspace-scoped factory; resolve it per invocation instead.
        builder.Services.AddScoped<IntoChat.Marketplace.AppTools>();
        builder.Services.AddMcpServer().WithHttpTransport()
            .WithTools(CSharpToolService.Tools.Select(tool =>
                McpServerTool.Create(tool.Method, request => request.Services!.GetRequiredService<ScopedCSharpTools>())))
            .WithTools(IntoChat.Marketplace.AppTools.Methods.Select(method =>
                McpServerTool.Create(method, request => request.Services!.GetRequiredService<IntoChat.Marketplace.AppTools>())));
    }

    public static void MapCSharp(this IEndpointRouteBuilder routes)
    {
        routes.MapMcp("/workspaces/{workspaceId}/csharp-mcp").AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync).AddEndpointFilter(async (context, next) =>
            DeveloperModeEnabled(context.HttpContext.RequestServices) ? await next(context) : Results.NotFound());
        var files = routes.MapGroup("/workspaces/{workspaceId}/csharp").AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        files.AddEndpointFilter(async (context, next) =>
        {
            if (!DeveloperModeEnabled(context.HttpContext.RequestServices)) { return Results.NotFound(); }
            try { return await next(context); }
            catch (ArgumentException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
            catch (KeyNotFoundException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound); }
            catch (InvalidOperationException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
            catch (TimeoutException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status504GatewayTimeout); }
        });
        // ScopedCSharpTools resolves the workspace from the {workspaceId} route value.
        files.MapGet("/", async (ScopedCSharpTools tools, CancellationToken ct) => new { items = await tools.List(ct), allowActivation = tools.AllowActivation });
        files.MapGet("/{id}", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Read(id, ct));
        files.MapPut("/{id}", (string id, WriteCSharpFileRequest request, ScopedCSharpTools tools, CancellationToken ct)
            => tools.Write(id, request.Source, request.Name, request.Purpose, ct));
        files.MapPost("/{id}/start", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Start(id, ct));
        files.MapPost("/{id}/stop", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Stop(id, ct));
        files.MapDelete("/{id}", (string id, ScopedCSharpTools tools, CancellationToken ct) => tools.Delete(id, ct));
    }

    private static bool DeveloperModeEnabled(IServiceProvider services) =>
        AgentToolPolicy.DeveloperModeEnabled(services.GetRequiredService<IConfiguration>()["IntoChat:DeveloperMode"]);
}
