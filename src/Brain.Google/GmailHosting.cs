using Brain.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Google;

public static class GmailHosting
{
    public static ISiloBuilder AddBrainGmail(
        this ISiloBuilder silo,
        Func<IServiceProvider, IGmailMcpClient> mcpFactory)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(mcpFactory);
        silo.Services.AddSingleton(mcpFactory);
        return silo;
    }

    public static IServiceCollection AddGmailAgent(
        this IServiceCollection services,
        Func<IServiceProvider, IGmail> commandNeuronFactory,
        Func<IServiceProvider, SynapseMetadata> metadataFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commandNeuronFactory);
        ArgumentNullException.ThrowIfNull(metadataFactory);

        services.AddSingleton(sp =>
        {
            var chat = sp.GetRequiredService<IChatClient>();
            var mcp = sp.GetRequiredService<IGmailMcpClient>();
            var neuron = commandNeuronFactory(sp);
            return GmailMcpTools.CreateAgent(chat, mcp, neuron, () => metadataFactory(sp));
        });
        return services;
    }
}
