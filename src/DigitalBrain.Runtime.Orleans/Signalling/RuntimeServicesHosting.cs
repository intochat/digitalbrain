using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Storage;
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
        silo.Services.AddOptions<SessionSynapseOptions>().BindConfiguration(RuntimeOptions.From(silo.Services).SignallingSection);
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.AddSingleton<RuntimeClock>();
        silo.Services.AddSingleton<BrainListeners>();
        silo.Services.AddSingleton<IBrainListeners>(sp => sp.GetRequiredService<BrainListeners>());
        silo.Services.AddSingleton<ClusterListeners>();
        silo.Services.AddSingleton<DigitalBrain.Kernel.IClusterListeners>(sp => sp.GetRequiredService<ClusterListeners>());
        silo.Services.AddNeuronProxies();
        silo.Services.TryAddSingleton<DeliveryHub>();
        silo.Services.TryAddSingleton<IBrainObserver, ClusterBrainObserver>();
        silo.Services.AddSingleton<OperationRegistry>();
        silo.Services.TryAddSingleton<IOperationDriverLifetime, SiloDriverLifetime>();
        silo.Services.TryAddSingleton<IStorageOwnerSource, BrokerStorageOwners>();
        silo.Services.TryAddSingleton<IOperationHandler<WritePlan>, StorageWriteHandler>();
        silo.Services.AddSingleton(sp =>
        {
            var handlers = new OperationHandlers();
            var storageHandler = sp.GetService<IOperationHandler<WritePlan>>();
            if (storageHandler is not null)
            {
                handlers.Register(storageHandler);
            }
            return handlers;
        });
        silo.Services.AddJournalPayloads(typeof(OperationStarted).Assembly);

        silo.Services.TryAddSingleton<ApprovalWaiters>();
        silo.Services.AddSingleton(new NeuronBinding(typeof(IOperations), (sp, id) => new NeuronOperations(sp, id.Brain, id.ToString())));
        silo.Services.AddSingleton(new NeuronBinding(typeof(IOperationsQuery), (sp, id) => new NeuronOperationsQuery(sp, id.Brain)));
        silo.Services.AddSingleton(new NeuronBinding(typeof(ISignalHistory), (sp, id) => (sp.GetService<BrainSignalHistories>()
            ?? throw new InvalidOperationException("Signal history needs BrainLog as the cluster store (BrainStorageRegistration).")).For(id.Brain)));
        silo.Services.AddSingleton(new NeuronBinding(typeof(IConfirmations), (sp, id) =>
        {
            var req = NeuronHosting.Get<IConfirmationRequests>(sp, IConfirmationRequests.Of(id.Brain));
            var inbox = NeuronHosting.Get<IConfirmationInbox>(sp, IConfirmationRequests.Of(id.Brain));
            var policy = (sp.GetService(typeof(ConfirmationPolicies)) as ConfirmationPolicies)?.For(id.Brain.Value) ?? ConfirmationPolicy.DestructiveOnly;
            var waiters = sp.GetService<ApprovalWaiters>();
            return new AwaitedConfirmations(req, inbox, policy, waiters);
        }));

        return silo;
    }
}
