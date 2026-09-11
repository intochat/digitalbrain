using DigitalBrain.AI;
using DigitalBrain.UI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

namespace DigitalBrain.Kernel;

internal static class ConversationalAgentEndpoints
{
    private const string AgentName = "IntoCaht";

    public static void AddConversationalAgent(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAGUIServer();
        builder.Services.AddSingleton<WorkspaceArtifactStore>();
        builder.AddAIAgent(AgentName, static (services, name) => ConversationalAgent.Create(services, name,
            [.. services.GetService<TableService>() is { } tables ? new TableAgentTools(tables).Create() : [],
             .. new WorkspaceAgentTools(services.GetRequiredService<WorkspaceArtifactStore>()).Create()]))
            // The existing BasicAuthGate protects a single-owner host. Do not infer a user
            // identity from client-supplied thread IDs. Multi-user hosting needs isolation.
            .WithInMemorySessionStore(withIsolation: false);
    }

    public static IEndpointConventionBuilder MapConversationalAgent(this IEndpointRouteBuilder endpoints)
        => endpoints.MapAGUIServer(AgentName, "/agent")
            .AddEndpointFilter(static async (context, next) =>
            {
                try
                {
                    var result = await next(context);
                    return result is IResult response ? new AgentStreamResult(response) : result;
                }
                catch (Exception error) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    return new AgentStreamResult(error);
                }
            });
}
