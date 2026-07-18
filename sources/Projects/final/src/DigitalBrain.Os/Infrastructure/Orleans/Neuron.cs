using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using Orleans.Streams;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DigitalBrain.Os.Infrastructure.Orleans;

public abstract class Neuron : Grain, INeuron, IAsyncObserver<Synapse>
{
    // Shared helpers for depth, world, self, timeline subscribe, emit, stamping.
    internal static class Core
    {
        internal static readonly AsyncLocal<Synapse?> Current = new();
        internal const int MaxDepth = 10;
        internal const string DepthKey = "db.depth";
        internal const string WorldKey = "db.world";
        internal const int MaxJournalEntries = 500;

        internal static NeuronId ToNeuronId(GrainId id) => new(id.Type.ToString()!, id.Key.ToString()!);

        // Self id from grain (used by Neuron and any derived).
        internal static NeuronId GetSelf(Grain grain)
        {
            var id = grain.GetGrainId();
            return ToNeuronId(id);
        }

        // Shared depth guard for Receive/Fire (de-dupe across bases).
        internal static bool CheckAndIncrementDepth(out int depth)
        {
            depth = RequestContext.Get(DepthKey) is int d ? d : 0;
            if (depth > MaxDepth) return false;
            RequestContext.Set(DepthKey, depth + 1);
            return true;
        }

        internal static void RestoreDepth(int previousDepth)
        {
            RequestContext.Set(DepthKey, previousDepth);
        }

        internal static string? GetCurrentWorldId() => RequestContext.Get(WorldKey) as string;

        internal static void SetCurrentWorldId(string? worldId)
        {
            RequestContext.Set(WorldKey, worldId ?? string.Empty);
        }

        // Shared activation helpers (subscribe to timeline only if the neuron declares IHandle<>s; always emit Activated synapse).
        internal static async Task SubscribeTimelineIfNeeded(Grain grain, Action<StreamSubscriptionHandle<Synapse>?> setHandle)
        {
            if (SynapseDispatch.HandledTypes(grain.GetType()).Count == 0) return;

            var stream = grain.GetStreamProvider(SynapseStream.ProviderName).Timeline();
            var observer = (IAsyncObserver<Synapse>)grain;

            // Explicit subscriptions survive deactivation (Orleans streaming contract), so reactivated neuron must
            // resume via GetAllSubscriptionHandles + ResumeAsync (official pattern) rather than re-Subscribe (avoids dups).
            // See MS Learn Orleans streaming APIs + StreamSubscriptionHandle.ResumeAsync.
            var existing = await stream.GetAllSubscriptionHandles();
            if (existing.Count == 0)
            {
                setHandle(await stream.SubscribeAsync(observer));
                return;
            }

            setHandle(await existing[0].ResumeAsync(observer));
            for (var i = 1; i < existing.Count; i++) await existing[i].UnsubscribeAsync();
        }

        internal static async Task EmitActivated(Grain grain, NeuronId self)
        {
            await grain.GetStreamProvider(SynapseStream.ProviderName).Timeline()
                .OnNextAsync(new Activated(self).Stamp(self));
        }

        // Shared stamp for Emit/Fire (used by both bases to avoid divergent stamp logic).
        internal static Synapse StampForRoute(Synapse synapse, NeuronId self, Synapse? current, RoutingMode routing, NeuronId receiver)
        {
            return synapse.Stamp(self, current) with
            {
                Metadata = (synapse.Metadata with { Receiver = receiver }) with { RoutingMode = routing }
            };
        }

        // Shared helper to emit the first-class SynapseIncoming wrapper.
        internal static Task EmitSynapseIncoming(Grain grain, Synapse synapse)
        {
            var self = GetSelf(grain);
            return grain.GetStreamProvider(SynapseStream.ProviderName).Timeline()
                .OnNextAsync(new SynapseIncoming(self, synapse).Stamp(self));
        }
    }

    private StreamSubscriptionHandle<Synapse>? _timelineSub;

    protected ILogger Logger => field ??= GrainContext.ActivationServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

    protected readonly Dictionary<string, string> State = [];

    // Per-grain durable journals. Incoming on receive, Outgoing on emit/fire. Isolation via JournalStore (registered in ConfigureDigitalBrainDefaults). Enables full causal replay for LlmAgent, Creator, Packager, history tools.
    protected IDurableList<Synapse> Incoming { get; private set; } = default!;
    protected IDurableList<Synapse> Outgoing { get; private set; } = default!;

    // Lifecycle event types (Activated, Deactivated, wrappers, telemetry). Enables GetLifecycleAsync for easy filtering by id/type (IAW/ino-style history access without all business synapses).
    protected static readonly HashSet<string> LifecycleSynapseNames =
    [
        with(StringComparer.OrdinalIgnoreCase), nameof(Activated), nameof(Deactivated), nameof(SynapseIncoming), nameof(SynapseOutgoing), nameof(NeuronTelemetry)
    ];

    protected NeuronId Self => Core.GetSelf(this);

    protected static string? CurrentWorldId => Core.GetCurrentWorldId();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Per-grain journals via the JournalStore registered in ConfigureDigitalBrainDefaults (Aspire + test + start).
        // GetOrCreate by Self ensures isolation (Incoming/Outgoing never shared across neurons); fixes prior global list interleaving.
        var js = ServiceProvider.GetService<DigitalBrain.Os.Infrastructure.Orleans.JournalStore>();
        if (js is not null)
        {
            var (inc, outg) = js.GetOrCreate(Self);
            Incoming = inc;
            Outgoing = outg;
        }
        else
        {
            // Fallback (misconfig or host without ConfigureDigitalBrainDefaults): use List as IDurableList stand-in so Emit never NREs.
            // All supported paths (Simulation, KernelHost, start.cs) register the store so this is not hit.
            Incoming = (IDurableList<Synapse>)(object)new List<Synapse>();
            Outgoing = (IDurableList<Synapse>)(object)new List<Synapse>();
        }

        await Core.SubscribeTimelineIfNeeded(this, h => _timelineSub = h);
        await Core.EmitActivated(this, Self);

        // Stage 1 durability: subclasses with IPersistentState can restore journal snapshots into the runtime
        // Incoming/Outgoing IDurableList so GetFullJournal/GetRecentHistory survive re-activation (and restart
        // when the "Default" storage is Redis on root).
        await RestoreJournalsFromSnapshotAsync();
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_timelineSub is not null)
        {
            try { await _timelineSub.UnsubscribeAsync(); }
            catch (Exception ex) { Logger.LogWarning(ex, "{Neuron}: timeline unsubscribe failed on deactivate", Self); }
            _timelineSub = null;
        }

        // Snapshot journals back to any persistent state the subclass owns (IPersistentState snapshot backing for the custom IDurableList journals).
        await SnapshotJournalsToStateAsync();

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task OnNextAsync(Synapse item, StreamSequenceToken? token = null) =>
        SynapseDispatch.HandledTypes(GetType()).Contains(item.GetType()) ? Receive(item) : Task.CompletedTask;

    public Task DeliverAsync(Synapse synapse) => Receive(synapse);

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        Logger.LogError(ex, "Timeline error in {Neuron}", Self);
        return Task.CompletedTask;
    }

    // Overridable hooks for grains that have IPersistentState<NeuronState> (or equivalent) to provide real durability
    // for the custom Incoming/Outgoing IDurableList journals via snapshots (backing the in-memory lists so history
    // survives re-activation and, when "Default" storage is Redis, kernel restarts).
    // Default is no-op (pure in-memory behavior for start.cs / TestCluster unless the grain opts in).
    protected virtual Task RestoreJournalsFromSnapshotAsync() => Task.CompletedTask;
    protected virtual Task SnapshotJournalsToStateAsync() => Task.CompletedTask;

    private async Task Receive(Synapse synapse)
    {
        if (!Core.CheckAndIncrementDepth(out var depth))
        {
            Logger.LogWarning("{Neuron}: depth limit hit, dropping {Synapse}", Self, synapse.GetType().Name);
            return;
        }

        var prev = Core.Current.Value;
        Core.Current.Value = synapse;

        // Append to this neuron's durable journal (per-grain list once JournalStore is active).
        Incoming.Add(synapse);

        // First-class SynapseIncoming wrapper is best-effort observability: a timeline failure here must be
        // logged, not swallowed, and must not abort the actual handler dispatch below.
        try { await Core.EmitSynapseIncoming(this, synapse); }
        catch (Exception ex) { Logger.LogError(ex, "{Neuron}: failed to emit SynapseIncoming for {Synapse}", Self, synapse.GetType().Name); }

        try
        {
            var synapseType = synapse.GetType().Name;
            var neuronType = GetType().Name;
            using var activity = NeuronInstrumentation.Source.StartActivity($"{synapseType} → {neuronType}");
            if (activity is not null)
            {
                activity.SetTag("neuron.type", Self.Type);
                activity.SetTag("neuron.key", Self.Key);
                activity.SetTag("db.world", CurrentWorldId);
                activity.SetTag("synapse.type", synapseType);
                activity.SetTag("db.correlation", synapse.Metadata.CorrelationId.ToString());
                activity.SetTag("db.causation", synapse.Metadata.CausationId.ToString());
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Dispatch(synapse);
            sw.Stop();
            NeuronInstrumentation.HandleDuration.Record(sw.Elapsed.TotalMilliseconds);
            NeuronInstrumentation.SynapsesIn.Add(1);
        }
        finally
        {
            Core.Current.Value = prev;
            Core.RestoreDepth(depth);
        }
    }

    protected Task Emit(Synapse synapse)
    {
        var stamped = Core.StampForRoute(synapse, Self, Core.Current.Value, RoutingMode.Broadcast, NeuronId.None);
        Outgoing.Add(stamped);
        NeuronInstrumentation.SynapsesOut.Add(1);
        return this.GetStreamProvider(SynapseStream.ProviderName).Timeline().OnNextAsync(stamped);
    }

    protected Task Ask(NeuronId target, Synapse synapse) => Fire(synapse, RoutingMode.PointToPoint, target);

    protected Task Ask<TTarget>(string key, Synapse synapse) where TTarget : INeuron
    {
        var target = GrainFactory.GetGrain<TTarget>(key);
        return Fire(synapse, RoutingMode.PointToPoint, Core.ToNeuronId(target.GetGrainId()));
    }

    protected Task Reply(Synapse synapse)
    {
        var caller = Core.Current.Value?.Metadata.Caller ?? NeuronId.None;
        return caller.IsNone ? Task.CompletedTask : Fire(synapse, RoutingMode.PointToPoint, caller);
    }

    private async Task Fire(Synapse synapse, RoutingMode routing, NeuronId receiver)
    {
        var stamped = Core.StampForRoute(synapse, Self, Core.Current.Value, routing, receiver);

        Outgoing.Add(stamped);
        NeuronInstrumentation.SynapsesOut.Add(1);

        if (routing == RoutingMode.Broadcast)
        {
            await this.GetStreamProvider(SynapseStream.ProviderName).Timeline().OnNextAsync(stamped);
        }
        else
        {
            var target = GrainFactory.GetGrain(GrainId.Create(receiver.Type, receiver.Key)).AsReference<INeuron>();
            await target.DeliverAsync(stamped);

            await this.GetStreamProvider(SynapseStream.ProviderName).Timeline()
                .OnNextAsync(new SynapseOutgoing(Self, stamped).Stamp(Self));
        }
    }

    private Task Dispatch(Synapse synapse) => SynapseDispatch.DispatchAsync(this, Logger, Self, synapse);

    // Journal access for this neuron's full lifecycle (incoming + outgoing synapses stamped with lineage).
    // Easy to get or filter by id/type (e.g. LifecycleSynapseNames). Used by LlmAgent, Creator, Packager observed, brain history, tests.
    public Task<IReadOnlyList<Synapse>> GetJournalHistoryAsync(int max = 20, CancellationToken cancellationToken = default)
    {
        var combined = Incoming.Concat(Outgoing)
            .OrderBy(x => x.Timestamp)
            .TakeLast(max)
            .ToList();
        return Task.FromResult<IReadOnlyList<Synapse>>(combined);
    }

    public Task<IReadOnlyList<Synapse>> GetFullJournalAsync(CancellationToken cancellationToken = default)
    {
        var combined = Incoming.Concat(Outgoing)
            .OrderBy(x => x.Timestamp)
            .ToList();
        return Task.FromResult<IReadOnlyList<Synapse>>(combined);
    }

    // Convenience for lifecycle subset (Activated etc) without the domain business synapses.
    public Task<IReadOnlyList<Synapse>> GetLifecycleAsync(int max = 50, CancellationToken cancellationToken = default)
    {
        var combined = Incoming.Concat(Outgoing)
            .Where(s => LifecycleSynapseNames.Contains(s.GetType().Name))
            .OrderBy(x => x.Timestamp)
            .TakeLast(max)
            .ToList();
        return Task.FromResult<IReadOnlyList<Synapse>>(combined);
    }

    public static class NeuronInstrumentation
    {
        public static readonly ActivitySource Source = new("DigitalBrain.Neuron");
        public static readonly Meter Meter = new("DigitalBrain.Neuron");
        public static readonly Counter<long> SynapsesIn = Meter.CreateCounter<long>("db.synapses.in");
        public static readonly Counter<long> SynapsesOut = Meter.CreateCounter<long>("db.synapses.out");
        public static readonly Histogram<double> HandleDuration = Meter.CreateHistogram<double>("db.handle.duration");
    }

    protected void Telemetry(string @event, Dictionary<string, string> data)
    {
        var self = Self;
        Emit(new NeuronTelemetry(self, @event, data));
        using var a = NeuronInstrumentation.Source.StartActivity(@event);
        if (a is not null)
        {
            a.SetTag("neuron.type", self.Type);
            a.SetTag("neuron.key", self.Key);
            a.SetTag("db.world", CurrentWorldId);
            foreach (var kv in data) a.SetTag(kv.Key, kv.Value);
        }
        NeuronInstrumentation.SynapsesOut.Add(1);
    }

}
