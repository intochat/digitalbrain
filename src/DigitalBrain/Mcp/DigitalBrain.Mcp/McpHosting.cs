using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Identity;
using ModelContextProtocol.AspNetCore;

namespace DigitalBrain.Mcp;

public static class McpHosting
{
    public static void AddDigitalBrainMcp(this IServiceCollection services)
    {
        services.AddIdentity();
        services.AddHttpContextAccessor();
        services.AddSingleton<McpCaller>();
        services.AddMcpServer().WithHttpTransport(options => options.Stateless = true)
            .WithTools<NeuronTools>().WithTools<McpCaller>();
    }

    public static void MapDigitalBrainMcp(this WebApplication app)
    {
        app.MapMcp("/mcp").AddEndpointFilter(async (context, next) =>
            await BrainAccessFilter.Decide(context.HttpContext, CallerContextStamper.Require().BrainId) is { } denied
                ? denied : await next(context));
        BrainRoutes.Group(app).MapMcp("/mcp");
    }
}
