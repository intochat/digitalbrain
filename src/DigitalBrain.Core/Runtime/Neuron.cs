using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Journaling;

namespace DigitalBrain;

public abstract class Neuron : DurableGrain, IGrainWithStringKey, Neuron.ITransport, Neuron.IDrainEntry
{
    internal const string DeliveryRejectedPrefix = "digitalbrain.delivery.rejected:";

    private static readonly ConcurrentDictionary<Type, Func<ITransport, Synapse, CancellationToken, Task>> WireDeliverers = new();

    private readonly Journal journal;
    private readonly Router router;
    private readonly ISynapseCodec codec;
    private readonly IEnvelopeCarrier envelopes;
    private readonly SpeechStager stager;
    private readonly Outbox outbox;
    private Turn? turn;
    private bool poisoned;

    protected Neuron()
    {
        var services = base.ServiceProvider;
        journal = services.GetRequiredService<Journal>();
        router = new Router(services.GetRequiredService<ICatalog>());
        codec = services.GetRequiredService<ISynapseCodec>();
        envelopes = services.GetRequiredService<IEnvelopeCarrier>();
        stager = new SpeechStager(journal, router, codec);
        outbox = new Outbox(this, journal, router, stager, codec, envelopes);
    }

    public NeuronId Id => new(NeuronId.KindOf(GetType()), this.GetPrimaryKeyString());

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());
        await base.OnActivateAsync(cancellationToken);
        journal.MarkCommitted();
        await outbox.ResumeAsync();
    }

    public sealed override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        => base.OnDeactivateAsync(reason, cancellationToken);

    [Obsolete("Core owns the single durable turn commit.", error: true)]
    protected new ValueTask WriteStateAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    [Obsolete("Neurons speak through the journaled bus.", error: true)]
    protected new IGrainFactory GrainFactory => throw new NotSupportedException();

    [Obsolete("Core owns activation lifetime.", error: true)]
    protected new void DeactivateOnIdle() => throw new NotSupportedException();

    [Obsolete("Core owns the activation service scope.", error: true)]
    protected new IServiceProvider ServiceProvider => throw new NotSupportedException();

    [Obsolete("Core owns the activation context.", error: true)]
    public new IGrainContext GrainContext => throw new NotSupportedException();

    [Obsolete("Core owns journal state registration.", error: true)]
    protected new IJournaledStateManager StateManager => throw new NotSupportedException();

    [Obsolete("Core owns journal state registration.", error: true)]
    protected new TState GetOrCreateState<TState>(string name) => throw new NotSupportedException();

    [Obsolete("Core owns journal state registration.", error: true)]
    protected new TState GetOrCreateState<TArg, TState>(
        string name, Func<TArg, TState> createState, TArg arg) => throw new NotSupportedException();

    [Obsolete("Core owns timer lifetime.", error: true)]
    protected new IDisposable RegisterTimer(
        Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();

    [Obsolete("Core owns timer lifetime.", error: true)]
    protected IGrainTimer RegisterGrainTimer<TState>(
        Func<TState, CancellationToken, Task> callback, TState state, GrainTimerCreationOptions options)
        => throw new NotSupportedException();

    protected void Emit(Synapse fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        RequireTurn().Speech.Add(fact);
    }

    internal static GrainId AddressOf(NeuronId id) => GrainId.Create(id.Kind, id.Name);

    internal IGrainFactory CoreGrainFactory => base.GrainFactory;

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

    internal async Task EmitIngressAsync(Synapse fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        RefusePoisoned();
        try
        {
            _ = stager.Stage(Id, fact, cause: null, TimeProvider.System.GetUtcNow());
            await outbox.PrepareCommitAsync();
            await CommitCoreAsync();
        }
        catch
        {
            Poison();
            throw;
        }

        outbox.Kick();
    }

    internal async Task CommitCoreAsync()
    {
        try
        {
            journal.SealV2Schema();
            await base.WriteStateAsync(CancellationToken.None);
            journal.MarkCommitted();
        }
        catch
        {
            Poison();
            throw;
        }
    }

    internal static Func<ITransport, Synapse, CancellationToken, Task> WireDelivererFor(Type factType)
        => WireDeliverers.GetOrAdd(factType, static type => Close<Func<ITransport, Synapse, CancellationToken, Task>>(
            nameof(SendAsync), type));

    [Alias("db.transport")]
    internal interface ITransport : IGrainWithStringKey
    {
        [Alias("deliver")]
        Task DeliverAsync<TFact>(TFact fact, CancellationToken cancellationToken)
            where TFact : Synapse;

        [ReadOnly]
        [Alias("read")]
        Task<NeuronReading> ReadAsync(long afterPosition);
    }

    [Alias("db.drain")]
    internal interface IDrainEntry : IGrainWithStringKey
    {
        [Alias("drain")]
        Task DrainAsync();
    }

    async Task ITransport.DeliverAsync<TFact>(TFact fact, CancellationToken cancellationToken)
    {
        var envelope = envelopes.Consume()
            ?? throw new InvalidOperationException("A delivery arrived without an envelope.");
        await ReceiveAsync(fact, envelope, cancellationToken);
    }

    Task<NeuronReading> ITransport.ReadAsync(long afterPosition)
    {
        RefusePoisoned();
        var facts = journal.Read(afterPosition)
            .Select(entry => new JournalFact(
                entry.Position,
                entry.Entry,
                entry.Kind,
                entry.ToEnvelope(Id).Metadata,
                entry.Cause?.ToSynapseRef(),
                entry.To?.Select(target => target.ToNeuronId()).ToArray(),
                codec.DecodeFact(entry.Kind, entry.Body)))
            .ToArray();
        return Task.FromResult(new NeuronReading(facts));
    }

    async Task IDrainEntry.DrainAsync()
    {
        RefusePoisoned();
        await outbox.DrainAsync(CancellationToken.None);
    }

    private async Task ReceiveAsync<TFact>(TFact fact, DeliveryEnvelope envelope, CancellationToken cancellationToken)
        where TFact : Synapse
    {
        ArgumentNullException.ThrowIfNull(fact);
        RefusePoisoned();
        if (envelope.Sequence <= journal.WatermarkOf(envelope.Source))
        {
            return;
        }

        if (!router.Listens(Id, typeof(TFact)))
        {
            throw new InvalidOperationException(
                $"{DeliveryRejectedPrefix}{Id} does not hear '{router.KindOf(typeof(TFact))}'.");
        }

        turn = new Turn(fact, envelope);
        try
        {
            await ((INeuron<TFact>)this).HandleAsync(fact, cancellationToken);
            await CommitTurnAsync();
        }
        catch
        {
            ClearTurn();
            throw;
        }
    }

    private async Task CommitTurnAsync()
    {
        var active = RequireTurn();
        try
        {
            var from = SynapseRefEntry.From(new SynapseRef(active.Envelope.Source, active.Envelope.Sequence));
            var heard = journal.AppendHeard(
                router.KindOf(active.Fact.GetType()),
                active.Envelope.Timestamp,
                from,
                active.Envelope.Cause is { } envelopeCause ? SynapseRefEntry.From(envelopeCause) : null,
                codec.Encode(active.Fact));
            var speechCause = new SynapseRefEntry(Id.Kind, Id.Name, heard);
            foreach (var speech in active.Speech)
            {
                _ = stager.Stage(Id, speech, speechCause, TimeProvider.System.GetUtcNow());
            }

            if (StateSlotIfTouched() is { } state)
            {
                journal.State = state;
            }

            journal.SetWatermark(active.Envelope.Source, active.Envelope.Sequence);
            await outbox.PrepareCommitAsync();
            await CommitCoreAsync();
        }
        catch
        {
            Poison();
            throw;
        }
        finally
        {
            ClearTurn();
        }

        outbox.Kick();
    }

    private Turn RequireTurn()
        => turn ?? throw new InvalidOperationException("Speech is valid only while handling a fact.");

    private void ClearTurn()
    {
        turn = null;
        ResetTurnState();
    }

    private void RefusePoisoned()
    {
        if (poisoned)
        {
            throw new InvalidOperationException($"{Id} is reloading after a failed durable commit.");
        }
    }

    internal void Poison()
    {
        poisoned = true;
        base.DeactivateOnIdle();
    }

    private protected virtual JsonElement? StateSlotIfTouched() => null;

    private protected virtual void ResetTurnState()
    {
    }

    private protected TValue MaterializeState<TValue>()
        where TValue : class, new()
    {
        _ = RequireTurn();
        var state = journal.CommittedState;
        return state.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new TValue()
            : (TValue?)codec.Decode(state, typeof(TValue)) ?? new TValue();
    }

    private protected JsonElement EncodeState(object state) => codec.Encode(state);

    private static TDelegate Close<TDelegate>(string method, params Type[] typeArguments)
        where TDelegate : Delegate
        => typeof(Neuron)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArguments)
            .CreateDelegate<TDelegate>();

    private static Task SendAsync<TFact>(ITransport receiver, Synapse fact, CancellationToken cancellationToken)
        where TFact : Synapse
        => receiver.DeliverAsync((TFact)fact, cancellationToken);

    private sealed class Turn(Synapse fact, DeliveryEnvelope envelope)
    {
        internal Synapse Fact { get; } = fact;

        internal DeliveryEnvelope Envelope { get; } = envelope;

        internal List<Synapse> Speech { get; } = [];
    }
}
