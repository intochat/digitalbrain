using System.Diagnostics;
using System.Diagnostics.Metrics;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DigitalBrain.Kernel.Runtime;
using Orleans.Runtime;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

public sealed class NeuronLifecycleOptions
{
    public bool PersistActivationMarkers { get; set; }
    public int MaximumRetainedSynapsesPerDirection { get; set; } = 512;
    public int MaximumTimelinePlaintextBytes { get; set; } = 2 * 1024 * 1024;
}

internal sealed record NeuronTimelineState(
    long Revision,
    long DroppedIncoming,
    long DroppedOutgoing,
    Synapse[] Incoming,
    Synapse[] Outgoing)
{
    public static NeuronTimelineState Empty() => new(0, 0, 0, [], []);
}

[GrainType("digitalbrain.base.v2")]
public abstract class Neuron(
    ILogger logger,
    IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector) : Grain, INeuron, IAsyncObserver<Synapse>
{
    protected readonly ILogger Logger = logger;
    private EncryptedPersistentState<NeuronTimelineState>? _timelineState;
    private NeuronTimelineState _timeline = NeuronTimelineState.Empty();
    private StreamSubscriptionHandle<Synapse>? _timelineSubscription;
    private NeuronLifecycleOptions _lifecycle = new();

    private EncryptedPersistentState<NeuronTimelineState> TimelineState => _timelineState ??= new(
        persistentState,
        protector,
        RuntimeStateKeys.SynapseTimeline(Self.Value),
        RuntimeStateKinds.SynapseTimeline,
        RuntimeStateSchemas.SynapseTimeline,
        NeuronTimelineState.Empty,
        static value => value.Revision,
        ValidateTimeline);

    // The synapse currently being handled. Synapses fired while handling it are caused by it.
    // Grains are non-reentrant by Orleans contract, so plain field + finally-restore correctly nests causal chains.
    private Synapse? _currentCause;

    // The synapse currently being handled (the cause of anything fired while handling it), exposed so
    // subclasses doing manual point-to-point delivery can preserve causal lineage on stamped synapses.
    protected Synapse? CurrentCause => _currentCause;

    protected DateTimeOffset ActivatedAt { get; private set; }

    protected NeuronId Self => new(this.GetPrimaryKeyString() ?? this.GetGrainId().ToString());

    // Thin shared reply/context helper (item 13 continuation). IChannelNeuron impls use this to propagate causation/reply context via existing Stamp + CorrelationId/CausationId patterns.
    // Centralizes without duplication for cross-channel flows (e.g. viz -> chart UiSurface -> flutter).
    protected Synapse StampCurrent(Synapse s) => s.Stamp(Self, CurrentCause);

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        _lifecycle = ServiceProvider.GetService<IOptions<NeuronLifecycleOptions>>()?.Value ?? new NeuronLifecycleOptions();
        ValidateLifecycleOptions(_lifecycle);
        _timeline = await TimelineState.ReadAsync(ct);
        ActivatedAt = DateTimeOffset.UtcNow;

        if (_lifecycle.PersistActivationMarkers)
        {
            await AppendOutgoingAsync(new NeuronActivated(Self).Stamp(Self), ct);
            NeuronInstrumentation.SynapsesOut.Add(1);
        }

        await SubscribeTimelineIfNeeded(ct);
    }

    protected virtual bool ShouldSubscribeToTimeline => SynapseDispatch.HandledTypes(GetType()).Count > 0;

    // Subscribe to the broadcast timeline when ShouldSubscribeToTimeline says so, so point-to-point-only
    // neurons are unaffected. Explicit subscriptions survive deactivation (Orleans streaming contract), so
    // a reactivated neuron resumes via GetAllSubscriptionHandles + ResumeAsync rather than re-subscribing
    // (avoids duplicate deliveries). Silos that don't register the timeline provider (minimal/legacy test
    // hosts) degrade gracefully: the neuron activates without broadcast reception instead of failing.
    private async Task SubscribeTimelineIfNeeded(CancellationToken cancellationToken)
    {
        if (!ShouldSubscribeToTimeline)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        IAsyncStream<Synapse> stream;
        try
        {
            stream = this.GetStreamProvider(SynapseStream.ProviderName).Timeline();
        }
        catch (KeyNotFoundException)
        {
            Logger.LogDebug("Timeline provider '{Provider}' not registered for {Neuron}; broadcast reception disabled.", SynapseStream.ProviderName, Self);
            return;
        }

        var existing = await stream.GetAllSubscriptionHandles();
        cancellationToken.ThrowIfCancellationRequested();
        if (existing.Count == 0)
        {
            _timelineSubscription = await stream.SubscribeAsync(this);
            return;
        }

        _timelineSubscription = await existing[0].ResumeAsync(this);
        for (var i = 1; i < existing.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await existing[i].UnsubscribeAsync();
        }
    }

    // Broadcast reception mirrors DeliverAsync's point-to-point contract: record the observed synapse
    // in the incoming timeline first (so GetIncomingTimelineAsync reflects everything this neuron has
    // witnessed, not just what it had a declared handler for), then dispatch to it if applicable.
    protected Task RecordBroadcastReceivedAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return AppendIncomingAsync(synapse, cancellationToken);
    }

    // Default broadcast reception: dispatch only synapse types this neuron statically declares IHandle<T> for.
    // Dynamic hosts override to filter through their own runtime manifest instead.
    protected Task DispatchBroadcastIfHandledAsync(Synapse item, CancellationToken cancellationToken = default) =>
        SynapseDispatch.HandledTypes(GetType()).Contains(item.GetType())
            ? SynapseDispatch.DispatchAsync(this, Logger, Self, item, cancellationToken)
            : Task.CompletedTask;

    public virtual async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);
        await DispatchBroadcastIfHandledAsync(item);
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        Logger.LogError(ex, "Timeline stream error in {Neuron}", Self);
        return Task.CompletedTask;
    }

    public async Task FireAsync<T>(T payload, CancellationToken cancellationToken = default) where T : Synapse
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stamped = payload.Stamp(Self, _currentCause);
        await AppendOutgoingAsync(stamped, cancellationToken);

        if (stamped.IsBroadcast)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this.GetStreamProvider(SynapseStream.ProviderName).Timeline().OnNextAsync(stamped);
        }
        else if (stamped.Receiver is not null)
        {
            var target = GrainFactory.GetGrain<INeuron>(stamped.Receiver.Value);
            await target.DeliverAsync(stamped, cancellationToken);
        }
        else
        {
            await DeliverAsync(stamped, cancellationToken);
        }

        NeuronInstrumentation.SynapsesOut.Add(1);
        Logger.LogDebug("Fired {Type} from {Self}", typeof(T).Name, Self);
    }

    protected Task Broadcast(Synapse s, CancellationToken cancellationToken = default) => FireAsync(s with { IsBroadcast = true }, cancellationToken);

    public Task<IReadOnlyList<Synapse>> GetTimelineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(_timeline.Outgoing));

    public Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(_timeline.Incoming));

    public Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(_timeline.Outgoing));

    public Task<IReadOnlyList<Synapse>> GetCausalLineageAsync(string correlationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(_timeline.Outgoing
            .Where(s => s.CorrelationId == correlationId || s.SynapseId == correlationId)
            .Concat(_timeline.Incoming.Where(s => s.CorrelationId == correlationId || s.SynapseId == correlationId))
            .OrderBy(s => s.Timestamp)
            .DistinctBy(s => s.SynapseId)
            .ToList()));

    public Task<IReadOnlyList<Synapse>> GetTimelineForCorrelationAsync(string correlationId, CancellationToken cancellationToken = default) =>
        GetCausalLineageAsync(correlationId, cancellationToken);

    public Task<string> GetSiloIdentityAsync(CancellationToken cancellationToken = default) => Task.FromResult(
        GrainContext.Address.SiloAddress?.ToString()
            ?? throw new InvalidOperationException($"SiloAddress unavailable for activated grain {Self}."));

    public async Task<Checkpoint> CreateCheckpointAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Dedup by the stable SynapseId (a synapse fired then self-delivered appears in both timelines as the
        // same instance) — robust vs. the old {Timestamp,Type,Sender,Receiver} heuristic.
        var snap = _timeline.Outgoing.Concat(_timeline.Incoming).DistinctBy(s => s.SynapseId).ToList();
        var cp = new Checkpoint(Self, snap.AsReadOnly(), DateTimeOffset.UtcNow);
        await FireAsync(cp, cancellationToken);
        return cp;
    }

    public async Task<NeuronId> BranchAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var branchKey = $"{Self.Value}@branch-{Guid.NewGuid():N}";
        // Branch into a NEW grain of the SAME concrete type as this neuron (was hardcoded to IDemoNeuron),
        // so the fork really is a copy of *this* neuron's behavior, replayed from the checkpoint.
        var branch = GrainFactory.GetGrain<INeuron>(GrainId.Create(this.GetGrainId().Type, branchKey));
        foreach (var s in checkpoint.Snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await branch.DeliverAsync(s, cancellationToken);
        }
        await branch.FireAsync(new BranchCreated(Self, branchKey), cancellationToken);
        return new NeuronId(branchKey);
    }

    // Restore: seed this neuron's incoming timeline from a checkpoint WITHOUT re-dispatching handlers
    // (state recovery, not re-execution). Branching, by contrast, replays into a fresh grain.
    public async Task RestoreCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UpdateTimelineAsync(
            current => CompactTimeline(current with
            {
                Revision = checked(current.Revision + 1),
                Incoming = [.. current.Incoming, .. checkpoint.Snapshot]
            }),
            cancellationToken);
    }

    // Internal for point to point. Incoming synapses are auto-recorded here (called by sender Fire or direct).
    public async Task DeliverAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await AppendIncomingAsync(synapse, cancellationToken);

        var previousCause = _currentCause;
        _currentCause = synapse;
        try
        {
            var synapseType = synapse.GetType().Name;
            var neuronType = GetType().Name;
            using var activity = NeuronInstrumentation.Source.StartActivity($"{synapseType} \u2192 {neuronType}");
            if (activity is not null)
            {
                activity.SetTag("neuron.id", Self.Value);
                activity.SetTag("synapse.type", synapseType);
                if (!string.IsNullOrEmpty(synapse.CorrelationId))
                {
                    activity.SetTag("correlation.id", synapse.CorrelationId);
                }
            }

            var handleStopwatch = Stopwatch.StartNew();
            if (!await TryHandleViaDeclaredInterfaceAsync(synapse, cancellationToken))
            {
                await DispatchSynapse(synapse, cancellationToken);
            }
            handleStopwatch.Stop();

            NeuronInstrumentation.HandleDuration.Record(handleStopwatch.Elapsed.TotalMilliseconds);
            NeuronInstrumentation.SynapsesIn.Add(1);
        }
        finally
        {
            _currentCause = previousCause;
        }
    }

    protected virtual Task DispatchSynapse(Synapse synapse, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Tries to locate and invoke IHandle<T>.HandleAsync via declared interfaces on this grain (prototype path).
    // Concrete grains should prefer listing IHandle<T> so Orleans + source-gen can handle; this remains for flexibility with dynamic synapses.
    // Logs at Debug when used so prototype reliance is observable.
    private async ValueTask<bool> TryHandleViaDeclaredInterfaceAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        var grainType = GetType();
        foreach (var iface in grainType.GetInterfaces())
        {
            if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(IHandle<>))
            {
                continue;
            }

            var handledType = iface.GetGenericArguments()[0];
            if (handledType != synapse.GetType() && !handledType.IsAssignableFrom(synapse.GetType()))
            {
                continue;
            }

            var handleMethod = iface.GetMethod("HandleAsync", new[] { handledType, typeof(CancellationToken) });
            if (handleMethod is null)
            {
                continue;
            }

            Logger.LogDebug("Reflection IHandle<> dispatch for synapse {Type} on {GrainType}", synapse.Type, grainType.Name);
            var result = handleMethod.Invoke(this, new object[] { synapse, cancellationToken });
            if (result is Task t)
            {
                await t;
            }
            else if (result is ValueTask vt)
            {
                await vt;
            }

            return true;
        }
        return false;
    }

    private Task AppendIncomingAsync(Synapse synapse, CancellationToken cancellationToken) =>
        UpdateTimelineAsync(
            current => CompactTimeline(current with
            {
                Revision = checked(current.Revision + 1),
                Incoming = [.. current.Incoming, synapse]
            }, synapse.SynapseId),
            cancellationToken);

    private Task AppendOutgoingAsync(Synapse synapse, CancellationToken cancellationToken) =>
        UpdateTimelineAsync(
            current => CompactTimeline(current with
            {
                Revision = checked(current.Revision + 1),
                Outgoing = [.. current.Outgoing, synapse]
            }, synapse.SynapseId),
            cancellationToken);

    private NeuronTimelineState CompactTimeline(NeuronTimelineState state, string? requiredSynapseId = null)
    {
        var incoming = state.Incoming;
        var outgoing = state.Outgoing;
        var droppedIncoming = state.DroppedIncoming;
        var droppedOutgoing = state.DroppedOutgoing;

        if (incoming.Length > _lifecycle.MaximumRetainedSynapsesPerDirection)
        {
            var remove = incoming.Length - _lifecycle.MaximumRetainedSynapsesPerDirection;
            incoming = incoming[remove..];
            droppedIncoming = checked(droppedIncoming + remove);
        }
        if (outgoing.Length > _lifecycle.MaximumRetainedSynapsesPerDirection)
        {
            var remove = outgoing.Length - _lifecycle.MaximumRetainedSynapsesPerDirection;
            outgoing = outgoing[remove..];
            droppedOutgoing = checked(droppedOutgoing + remove);
        }

        var compacted = state with
        {
            DroppedIncoming = droppedIncoming,
            DroppedOutgoing = droppedOutgoing,
            Incoming = incoming,
            Outgoing = outgoing
        };

        while (protector.MeasurePlaintextBytes(compacted) > _lifecycle.MaximumTimelinePlaintextBytes)
        {
            var incomingCandidate = FirstRemovable(incoming, requiredSynapseId);
            var outgoingCandidate = FirstRemovable(outgoing, requiredSynapseId);
            if (incomingCandidate < 0 && outgoingCandidate < 0)
                throw new InvalidOperationException("A synapse exceeds the neuron timeline retention bound.");

            if (outgoingCandidate < 0 || incomingCandidate >= 0 &&
                incoming[incomingCandidate].Timestamp <= outgoing[outgoingCandidate].Timestamp)
            {
                incoming = RemoveAt(incoming, incomingCandidate);
                droppedIncoming = checked(droppedIncoming + 1);
            }
            else
            {
                outgoing = RemoveAt(outgoing, outgoingCandidate);
                droppedOutgoing = checked(droppedOutgoing + 1);
            }

            compacted = compacted with
            {
                DroppedIncoming = droppedIncoming,
                DroppedOutgoing = droppedOutgoing,
                Incoming = incoming,
                Outgoing = outgoing
            };
        }

        return compacted;
    }

    private static int FirstRemovable(IReadOnlyList<Synapse> timeline, string? requiredSynapseId)
    {
        for (var i = 0; i < timeline.Count; i++)
            if (!string.Equals(timeline[i].SynapseId, requiredSynapseId, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static Synapse[] RemoveAt(Synapse[] timeline, int index) =>
        [.. timeline.Take(index), .. timeline.Skip(index + 1)];

    private async Task UpdateTimelineAsync(
        Func<NeuronTimelineState, NeuronTimelineState> transition,
        CancellationToken cancellationToken)
    {
        _timeline = await TimelineState.UpdateAsync(
            _timeline.Revision,
            current =>
            {
                var next = transition(current);
                return (next, next);
            },
            cancellationToken);
    }

    private void ValidateTimeline(NeuronTimelineState state)
    {
        if (state.Revision < 0 || state.DroppedIncoming < 0 || state.DroppedOutgoing < 0 ||
            state.Incoming is null || state.Outgoing is null ||
            state.Incoming.Length > _lifecycle.MaximumRetainedSynapsesPerDirection ||
            state.Outgoing.Length > _lifecycle.MaximumRetainedSynapsesPerDirection ||
            state.Incoming.Any(static synapse => synapse is null) ||
            state.Outgoing.Any(static synapse => synapse is null) ||
            protector.MeasurePlaintextBytes(state) > _lifecycle.MaximumTimelinePlaintextBytes)
            throw new InvalidOperationException("Neuron timeline state is invalid.");
    }

    private static void ValidateLifecycleOptions(NeuronLifecycleOptions options)
    {
        if (options.MaximumRetainedSynapsesPerDirection < 1 ||
            options.MaximumTimelinePlaintextBytes is < 1024 or > EncryptedRuntimeStateProtector.MaximumProtectedPlaintextBytes)
            throw new InvalidOperationException("Neuron timeline retention options are invalid.");
    }

    private static IReadOnlyList<Synapse> SnapshotTimeline(IEnumerable<Synapse> timeline)
        => timeline.ToArray();

    public static class NeuronInstrumentation
    {
        public static readonly ActivitySource Source = new("DigitalBrain.Neuron");
        public static readonly Meter Meter = new("DigitalBrain.Neuron");
        public static readonly Counter<long> SynapsesIn = Meter.CreateCounter<long>("db.synapses.in");
        public static readonly Counter<long> SynapsesOut = Meter.CreateCounter<long>("db.synapses.out");
        public static readonly Histogram<double> HandleDuration = Meter.CreateHistogram<double>("db.handle.duration");
    }
}
