using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Integrations.Tests;

[ClientEntryPoint]
[Alias("integrations.driver")]
[Description("Integration harness driver neuron")]
public partial interface IIntegrationDriver : INeuron
{
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class IntegrationDriver :
    Neuron,
    IIntegrationDriver,
    IHandle<SalesforceMutationApproval>
{
    public Task HandleAsync(SalesforceMutationApproval synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed partial class IntegrationsHarnessModule : IModule
{
    // OS.UiEdge (and other product assemblies) load AI grain contracts into the AppDomain even when
    // AIModule is not selected. Register the JSON codecs those grain methods require.
    static partial void ConfigureSerialization(IServiceCollection services)
        => services.AddSerializer(
            serializer => serializer.AddJsonSerializer(
                static type => type == typeof(Microsoft.Extensions.AI.ChatMessage)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponse)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponseUpdate)
                    || typeof(Microsoft.Extensions.AI.AIContent).IsAssignableFrom(type)));
}
