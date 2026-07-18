using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Brain.AgentGateway;

public static class AgentGatewayHosting
{
    public static WebApplicationBuilder AddAgentGateway(this WebApplicationBuilder builder)
    {
        builder.UseOrleansClient(client =>
        {
            if (builder.Configuration.GetValue("Orleans:UseLocalhostClustering", false))
            {
                client.UseLocalhostClustering(
                    clusterId: builder.Configuration["Orleans:ClusterId"] ?? "dev",
                    serviceId: builder.Configuration["Orleans:ServiceId"] ?? "dev");
            }
        });

        builder.Services.AddSingleton<IChatClient, GroupChatNeuronChatClient>();
        builder.Services.AddAIAgent(
            name: "group-chat-neuron",
            instructions: "Development agent backed by typed IGroupChat Orleans neuron references.",
            chatClientServiceKey: null);
        return builder;
    }
}
