using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Integrations.Tests;

[ClientEntryPoint]
[Alias("integrations.driver")]
[Description("Integration harness driver neuron")]
public partial interface IIntegrationDriver : INeuron
{
}

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

public sealed partial class IntegrationsHarnessModule : IModule;
