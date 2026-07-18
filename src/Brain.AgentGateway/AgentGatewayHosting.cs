using Brain.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Brain.AgentGateway;

public static class AgentGatewayHosting
{
    public static IServiceCollection AddAgentGateway(this IServiceCollection services)
    {
        services.AddSingleton<TypedNeuronAgentAdapter>();
        return services;
    }
}

public sealed class TypedNeuronAgentAdapter(IClusterClient clusterClient)
{
    public IClusterClient ClusterClient { get; } = clusterClient;

    public Brain.Client.Brain CreateBrain() => new(ClusterClient);
}
