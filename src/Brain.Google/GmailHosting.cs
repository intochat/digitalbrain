using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Google;

public static class GmailHosting
{
    public static ISiloBuilder AddBrainGmail(this ISiloBuilder silo, Func<IServiceProvider, IGmailMcpClient>? mcpFactory = null)
    {
        if (mcpFactory is null)
            silo.Services.AddSingleton<IGmailMcpClient, FakeGmailMcpClient>();
        else
            silo.Services.AddSingleton(mcpFactory);

        return silo;
    }

    public static IServiceCollection AddGmailAgent(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var chat = sp.GetRequiredService<IChatClient>();
            var mcp = sp.GetRequiredService<IGmailMcpClient>();
            return GmailMcpTools.CreateAgent(chat, mcp);
        });
        return services;
    }
}
