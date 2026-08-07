using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain;

[GrainType(GrainTypeName)]
internal sealed class NeuronHost : DurableGrain, INeuronHost
{
    internal const string GrainTypeName = "digitalbrain.neuron-host";

    private static readonly ConcurrentDictionary<Type, Func<INeuronHost, Synapse, CancellationToken, Task<DeliveryResult>>> WireDeliverers = new();
    private static readonly ConcurrentDictionary<Type, Func<Neuron, Synapse, CancellationToken, Task>> BehaviorHandlers = new();

    private readonly Journal journal;
    private readonly Router router;
    private readonly CompositionCatalog catalog;
    private readonly ISynapseSerialization serialization;
    private readonly IEnvelopeCarrier envelopes;
    private readonly DigitalBrainClock clock;
    private readonly ProducedSynapseStager stager;
    private readonly Outbox outbox;
    private bool poisoned;

    public NeuronHost()
    {
        var services = base.ServiceProvider;
        journal = services.GetRequiredService<Journal>();
        router = services.GetRequiredService<Router>();
        catalog = services.GetRequiredService<CompositionCatalog>();
        serialization = services.GetRequiredService<ISynapseSerialization>();
        envelopes = services.GetRequiredService<IEnvelopeCarrier>();
        clock = services.GetRequiredService<DigitalBrainClock>();
        stager = new ProducedSynapseStager(journal, router, serialization);
        outbox = new Outbox(this, journal, router, serialization, envelopes, clock);
    }

    internal ScopedNeuronAddress Address
        => ScopedNeuronAddressCodec.Decode(this.GetPrimaryKeyString());

    internal NeuronId Id => Address.Neuron;

    internal ScopeKey Scope => Address.Scope;

    internal static GrainId AddressOf(ScopedNeuronAddress address)
        => GrainId.Create(GrainTypeName, ScopedNeuronAddressCodec.Encode(address));

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        journal.MarkRecorded();
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        => base.OnDeactivateAsync(reason, cancellationToken);

    internal IGrainFactory RuntimeGrainFactory => base.GrainFactory;

    internal IGrainTimer RegisterOutboxTimer(
        Func<Outbox, CancellationToken, Task> callback,
        Outbox state)
        => GrainBaseExtensions.RegisterGrainTimer(
            this,
            callback,
            state,
            new GrainTimerCreationOptions
            {
                DueTime = DeliveryPolicy.RetryInterval,
                Period = DeliveryPolicy.RetryInterval,
            });

    internal async Task RecordAsync()
    {
        try
        {
            journal.SealSchema();
            await base.WriteStateAsync(CancellationToken.None);
            journal.MarkRecorded();
        }
        catch
        {
            Poison();
            throw;
        }
    }

    internal void ProduceDeliveryFailure(
        SynapseReference failedSynapse,
        NeuronId receiver,
        string reason,
        int attempts,
        SynapseReference causedBy)
        => _ = stager.StageDeliveryFailure(
            Id,
            new DeliveryFailed(failedSynapse, receiver, reason, attempts),
            causedBy,
            clock.UtcNow);

    internal void Poison()
    {
        poisoned = true;
        base.DeactivateOnIdle();
    }

    internal static Func<INeuronHost, Synapse, CancellationToken, Task<DeliveryResult>> WireDelivererFor(Type synapseType)
        => WireDeliverers.GetOrAdd(synapseType, static type => Close<Func<INeuronHost, Synapse, CancellationToken, Task<DeliveryResult>>>(
            nameof(SendAsync), type));

    async Task<DeliveryResult> INeuronHost.DeliverAsync<TSynapse>(TSynapse synapse, CancellationToken cancellationToken)
    {
        var envelope = envelopes.Consume()
            ?? throw new InvalidOperationException("A delivery arrived without an envelope.");
        return await ReceiveAsync(synapse, envelope, cancellationToken);
    }

    Task INeuronHost.PublishAsync(Synapse synapse) => PublishSourceAsync(synapse);

    Task<JournalRead> INeuronHost.ReadAsync(long afterPosition, int maximumRecords)
    {
        RefusePoisoned();
        return Task.FromResult(journal.Read(afterPosition, maximumRecords));
    }

    Task INeuronHost.DrainAsync() => outbox.DrainAsync(CancellationToken.None);

    private async Task PublishSourceAsync(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        RefusePoisoned();
        if (!SynapseSourceIdentity.Is(Id))
        {
            throw new InvalidOperationException($"{Id} is not a source identity.");
        }

        try
        {
            _ = stager.StageIngress(
                Id,
                synapse,
                causedBy: null,
                clock.UtcNow);
            await outbox.PrepareRecordAsync();
            await RecordAsync();
        }
        catch
        {
            Poison();
            throw;
        }

        outbox.Kick();
    }

    private async Task<DeliveryResult> ReceiveAsync<TSynapse>(
        TSynapse synapse,
        DeliveryEnvelope envelope,
        CancellationToken cancellationToken)
        where TSynapse : Synapse
    {
        ArgumentNullException.ThrowIfNull(synapse);
        RefusePoisoned();
        if (SynapseSourceIdentity.Is(Id))
        {
            return DeliveryResult.Terminal($"{Id} is a source identity, not a behavior.");
        }

        if (envelope.Sequence <= journal.WatermarkOf(envelope.Source))
        {
            return DeliveryResult.Success;
        }

        if (!router.Listens(Id, typeof(TSynapse)))
        {
            return DeliveryResult.Terminal(
                $"{Id} does not handle '{router.KindOf(typeof(TSynapse))}'.");
        }

        using var behaviorScope = base.ServiceProvider.CreateScope();
        behaviorScope.ServiceProvider.GetRequiredService<WorkspaceBindingHolder>().Bind(Scope);
        var behavior = catalog.CreateBehavior(Id.Kind, behaviorScope.ServiceProvider);
        var binding = new TurnBinding(
            Id,
            new SynapseOrigin(envelope.Source, envelope.Sequence, envelope.OccurredAt, envelope.Authority),
            journal,
            serialization);
        behavior.Bind(binding);
        try
        {
            await BehaviorHandlerFor(typeof(TSynapse))(behavior, synapse, cancellationToken);
        }
        finally
        {
            behavior.Unbind(binding);
        }

        return await RecordTurnAsync(synapse, envelope, binding);
    }

    private async Task<DeliveryResult> RecordTurnAsync<TSynapse>(
        TSynapse received,
        DeliveryEnvelope envelope,
        TurnBinding binding)
        where TSynapse : Synapse
    {
        try
        {
            foreach (var produced in binding.Staged)
            {
                stager.ValidateForRecording(Id, produced.Synapse, produced.Dispatch);
            }
        }
        catch (Exception rejection) when (rejection is DirectDispatchRejectedException or AuthoredSynapseRejectedException)
        {
            return DeliveryResult.Reject(rejection.Message);
        }

        try
        {
            var receivedPosition = journal.AppendReceived(
                router.KindOf(received.GetType()),
                new SynapseOrigin(envelope.Source, envelope.Sequence, envelope.OccurredAt, envelope.Authority),
                envelope.CausedBy,
                serialization.Serialize(received));
            var causedBy = new SynapseReference(Id, receivedPosition);
            foreach (var produced in binding.Staged)
            {
                _ = stager.StageAuthored(
                    Id,
                    produced.Synapse,
                    produced.Dispatch,
                    causedBy,
                    clock.UtcNow);
            }

            if (binding.SerializeTouchedState() is { } state)
            {
                journal.State = state;
            }

            journal.SetWatermark(envelope.Source, envelope.Sequence);
            await outbox.PrepareRecordAsync();
            await RecordAsync();
        }
        catch
        {
            Poison();
            throw;
        }

        outbox.Kick();
        return DeliveryResult.Success;
    }

    private void RefusePoisoned()
    {
        if (poisoned)
        {
            throw new InvalidOperationException($"{Id} is reloading after a failed durable recording.");
        }
    }

    private static Func<Neuron, Synapse, CancellationToken, Task> BehaviorHandlerFor(Type synapseType)
        => BehaviorHandlers.GetOrAdd(synapseType, static type => Close<Func<Neuron, Synapse, CancellationToken, Task>>(
            nameof(HandleAsync), type));

    private static TDelegate Close<TDelegate>(string method, params Type[] typeArguments)
        where TDelegate : Delegate
        => typeof(NeuronHost)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArguments)
            .CreateDelegate<TDelegate>();

    private static Task<DeliveryResult> SendAsync<TSynapse>(INeuronHost receiver, Synapse synapse, CancellationToken cancellationToken)
        where TSynapse : Synapse
        => receiver.DeliverAsync((TSynapse)synapse, cancellationToken);

    private static Task HandleAsync<TSynapse>(Neuron behavior, Synapse synapse, CancellationToken cancellationToken)
        where TSynapse : Synapse
        => ((INeuron<TSynapse>)behavior).HandleAsync((TSynapse)synapse, cancellationToken);
}
