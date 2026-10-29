using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Spi;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>Hosts the runtime services (04-13): the session synapse neuron, the brain listener neuron and the operation entity. <c>AddDigitalBrainRuntime</c> calls it.</summary>
internal static class RuntimeServicesHosting
{
    internal static ISiloBuilder AddRuntimeServices(this ISiloBuilder silo)
    {
        silo.AddNeuronHosting(typeof(RuntimeServicesHosting).Assembly);
        silo.Services.AddOptions<SessionSynapseOptions>().BindConfiguration(IntoChatOSNames.Signalling.Section);
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.AddSingleton<RuntimeClock>();
        silo.Services.AddSingleton<BrainListeners>();
        silo.Services.AddSingleton<IBrainListeners>(sp => sp.GetRequiredService<BrainListeners>());
        silo.Services.AddSingleton<ClusterListeners>();
        silo.Services.AddSingleton<DigitalBrain.Spi.IClusterListeners>(sp => sp.GetRequiredService<ClusterListeners>());
        silo.Services.AddNeuronProxies();
        silo.Services.TryAddSingleton<DeliveryHub>();
        silo.Services.TryAddSingleton<IBrainObserver, ClusterBrainObserver>();
        silo.Services.AddSingleton<OperationRegistry>();
        silo.Services.TryAddSingleton<IOperationGate, ConfirmingOperationGate>();
        silo.Services.AddJournalPayloads(typeof(OperationStarted).Assembly);
        return silo;
    }
}
