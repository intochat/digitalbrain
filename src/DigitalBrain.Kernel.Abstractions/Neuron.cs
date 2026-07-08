using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.Streams;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

public sealed class NeuronLifecycleOptions
{
    public bool JournalActivationMarkers { get; set; }
}

public sealed class NeuronJournals(
    [FromKeyedServices("in-journal")] IDurableList<Synapse> incoming,
    [FromKeyedServices("out-journal")] IDurableList<Synapse> outgoing)
{
    public IDurableList<Synapse> Incoming { get; } = incoming;
    public IDurableList<Synapse> Outgoing { get; } = outgoing;
}

[GrainType("digitalbrain.base.v2")]
public abstract class Neuron(ILogger logger, NeuronJournals journals) : DurableGrain, INeuron, IAsyncObserver<Synapse>
{
    protected readonly ILogger Logger = logger;
    private IDurableList<Synapse>? _incomingSynapses = journals.Incoming;
    private IDurableList<Synapse>? _outgoingSynapses = journals.Outgoing;
    private StreamSubscriptionHandle<Synapse>? _timelineSubscription;

    // The synapse currently being handled. Synapses fired while handling it are caused by it.
    // Grains are non-reentrant by Orleans contract, so plain field + finally-restore correctly nests causal chains.
    private Synapse? _currentCause;

    // The synapse currently being handled (the cause of anything fired while handling it), exposed so
    // subclasses doing manual point-to-point delivery can preserve causal lineage on stamped synapses.
    protected Synapse? CurrentCause => _currentCause;

    protected DateTimeOffset ActivatedAt { get; private set; }

    protected NeuronId Self => new(this.GetPrimaryKeyString() ?? this.GetGrainId().ToString());

    // Thin shared reply/context helper (item 13 continuation). IChannelNeuron impls (TelegramChatNeuron + FlutterUiNeuron)
    // use this to propagate causation/reply context via existing Stamp + CorrelationId/CausationId patterns.
    // Centralizes without duplication for cross-channel flows (e.g. Telegram viz -> chart UiSurface -> flutter).
    protected Synapse StampCurrent(Synapse s) => s.Stamp(Self, CurrentCause);

    // Dual journals (self-explanatory names): incoming received via Deliver, outgoing from our Fire calls.
    protected IDurableList<Synapse> IncomingJournal => _incomingSynapses ??= ResolveRequiredJournal("in-journal");
    protected IDurableList<Synapse> OutgoingJournal => _outgoingSynapses ??= ResolveRequiredJournal("out-journal");

    private IDurableList<Synapse> ResolveRequiredJournal(string key)
    {
        var journal = this.ServiceProvider.GetKeyedService<IDurableList<Synapse>>(key);
        if (journal is not null)
        {
            return journal;
        }

        // Fail fast: missing registration means wiring error. No silent in-memory degradation.
        throw new InvalidOperationException($"Required journal '{key}' not registered for {Self}. Ensure journal storage (AddAzureBlobJournalStorage + UseJsonJournalFormat or prototype for fast paths) registered on silo builder.");
    }

    private void AddToJournal(ref IDurableList<Synapse>? journalField, string key, Synapse synapse)
    {
        var target = journalField ??= ResolveRequiredJournal(key);
        try
        {
            target.Add(synapse);
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            // Fail fast instead of silent switch. Durability is required for causation and checkpoints.
            Logger.LogError(ex, "Journal writer not initialized for durable write of {Key} in {Neuron}.", key, Self);
            throw new InvalidOperationException($"Journal writer for '{key}' is not initialized for {Self}. Operation aborted to preserve durability guarantees.", ex);
        }
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _incomingSynapses ??= ResolveRequiredJournal("in-journal");
        _outgoingSynapses ??= ResolveRequiredJournal("out-journal");

        try
        {
            await base.OnActivateAsync(ct);
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogError(ex, "Journal state writer not initialized on activation for {Neuron}. Durability required.", Self);
            throw new InvalidOperationException($"Journal not ready on activation for {Self}.", ex);
        }

        ActivatedAt = DateTimeOffset.UtcNow;

        // Activation records are lifecycle metadata, not user synapse handling. Avoid durable writes
        // during Orleans journal recovery; only the prototype/in-memory harness opts into this marker.
        if (ServiceProvider.GetService<IOptions<NeuronLifecycleOptions>>()?.Value.JournalActivationMarkers == true)
        {
            try
            {
                AddToJournal(ref _outgoingSynapses, "out-journal", new NeuronActivated(Self).Stamp(Self));
                await WriteJournalStateAsync(ct);
                NeuronInstrumentation.SynapsesOut.Add(1);
            }
            catch (Exception ex) when (IsJournalWriterUninitialized(ex))
            {
                Logger.LogWarning(ex, "Activation marker was not journaled for {Neuron}; continuing so the first real synapse can initialize the journal.", Self);
            }
        }

        await SubscribeTimelineIfNeeded(ct);
    }

    // A neuron subscribes to the broadcast timeline iff it has a way to react to broadcasts. The default rule
    // is "declares at least one IHandle<T>"; dynamic hosts (GeneratedNeuron, whose handled types come from an
    // embodied pack's manifest, not static interfaces) override this to subscribe unconditionally.
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
    // in the incoming journal first (so GetIncomingTimelineAsync reflects everything this neuron has
    // witnessed, not just what it had a declared handler for), then dispatch to it if applicable.
    protected Task RecordBroadcastReceivedAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddToJournal(ref _incomingSynapses, "in-journal", synapse);
        return WriteJournalStateAsync(cancellationToken);
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
        AddToJournal(ref _outgoingSynapses, "out-journal", stamped);
        await WriteJournalStateAsync(cancellationToken);

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
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(OutgoingJournal));

    public Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(IncomingJournal));

    public Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(OutgoingJournal));

    public Task<IReadOnlyList<Synapse>> GetCausalLineageAsync(string correlationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Synapse>>(SnapshotTimeline(OutgoingJournal
            .Where(s => s.CorrelationId == correlationId || s.SynapseId == correlationId)
            .Concat(IncomingJournal.Where(s => s.CorrelationId == correlationId || s.SynapseId == correlationId))
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
        // Dedup by the stable SynapseId (a synapse fired then self-delivered appears in both journals as the
        // same instance) — robust vs. the old {Timestamp,Type,Sender,Receiver} heuristic.
        var snap = OutgoingJournal.Concat(IncomingJournal).DistinctBy(s => s.SynapseId).ToList();
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

    // Restore: seed this neuron's incoming journal from a checkpoint WITHOUT re-dispatching handlers
    // (state recovery, not re-execution). Branching, by contrast, replays into a fresh grain.
    public async Task RestoreCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        foreach (var s in checkpoint.Snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddToJournal(ref _incomingSynapses, "in-journal", s);
        }
        await WriteJournalStateAsync(cancellationToken);
    }

    // Internal for point to point. Incoming synapses are auto-recorded here (called by sender Fire or direct).
    public async Task DeliverAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddToJournal(ref _incomingSynapses, "in-journal", synapse);
        await WriteJournalStateAsync(cancellationToken);

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

    private async Task WriteJournalStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await WriteStateAsync(cancellationToken);
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogError(ex, "Journal state writer not initialized for durable WriteStateAsync in {Neuron}.", Self);
            throw new InvalidOperationException($"Durable journal writer not initialized for {Self}.", ex);
        }
    }

    private static bool IsJournalWriterUninitialized(Exception exception) =>
        exception.GetBaseException().Message.Contains("state journal stream writer is not initialized", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Synapse> SnapshotTimeline(IEnumerable<Synapse> journal)
        => journal.Select(CopyForOrleansResponse).ToArray();

    private static Synapse CopyForOrleansResponse(Synapse synapse) => synapse switch
    {
        Signal signal => signal with { Props = NormalizeStringObjectDictionary(signal.Props) },
        AskLlm ask => ask with { ReplyProps = NormalizeStringObjectDictionary(ask.ReplyProps) },
        ChartInteraction chart => chart with { Payload = NormalizeStringObjectDictionary(chart.Payload) },
        InoInteractResult result => CopyForOrleansResponse(result),
        Checkpoint checkpoint => checkpoint with { Snapshot = checkpoint.Snapshot.Select(CopyForOrleansResponse).ToArray() },
        _ => CopyUnknownSynapseForOrleansResponse(synapse)
    };

    private static InoInteractResult CopyForOrleansResponse(InoInteractResult result)
    {
        if (result.AvailableActions is null)
        {
            return result;
        }

        return result with { AvailableActions = result.AvailableActions.Select(CopyForOrleansResponse).ToArray() };
    }

    private static InoAction CopyForOrleansResponse(InoAction action) =>
        action.Props is null
            ? action
            : action with { Props = NormalizeStringObjectDictionary(action.Props) };

    private static Synapse CopyUnknownSynapseForOrleansResponse(Synapse synapse)
    {
        var normalized = NormalizeValue(synapse, new HashSet<object>(ReferenceEqualityComparer.Instance));
        if (!normalized.Changed || normalized.Value is not Synapse copy || ReferenceEquals(copy, synapse))
        {
            return synapse;
        }

        return copy with
        {
            Type = synapse.Type,
            Timestamp = synapse.Timestamp,
            Sender = synapse.Sender,
            Receiver = synapse.Receiver,
            IsBroadcast = synapse.IsBroadcast,
            CorrelationId = synapse.CorrelationId,
            SynapseId = synapse.SynapseId,
            CausationId = synapse.CausationId
        };
    }

    private static IReadOnlyDictionary<string, object?> NormalizeStringObjectDictionary(IReadOnlyDictionary<string, object?> dictionary)
    {
        var normalized = NormalizeStringObjectDictionaryValue(dictionary, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return normalized.Changed && normalized.Value is IReadOnlyDictionary<string, object?> normalizedDictionary
            ? normalizedDictionary
            : dictionary;
    }

    private static NormalizedValue NormalizeValue(object? value, HashSet<object> visited)
    {
        if (value is null)
        {
            return new(null, Changed: false);
        }

        if (value is JsonElement element)
        {
            return new(UnwrapJsonElement(element), Changed: true);
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or decimal or DateTime or DateTimeOffset or Guid or TimeSpan)
        {
            return new(value, Changed: false);
        }

        if (!visited.Add(value))
        {
            return new(value, Changed: false);
        }

        try
        {
            if (value is IReadOnlyDictionary<string, object?> objectDictionary)
            {
                return NormalizeStringObjectDictionaryValue(objectDictionary, visited);
            }

            if (value is IDictionary dictionary)
            {
                return NormalizeDictionaryValue(dictionary, visited);
            }

            if (value is IEnumerable enumerable)
            {
                return NormalizeEnumerableValue(enumerable, type, visited);
            }

            return ShouldInspectProperties(type)
                ? NormalizeDigitalBrainObject(value, type, visited)
                : new(value, Changed: false);
        }
        finally
        {
            visited.Remove(value);
        }
    }

    private static bool ShouldInspectProperties(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("DigitalBrain.", StringComparison.Ordinal);
    }

    private static NormalizedValue NormalizeStringObjectDictionaryValue(
        IReadOnlyDictionary<string, object?> dictionary,
        HashSet<object> visited)
    {
        var normalized = new Dictionary<string, object?>(dictionary.Count, DictionaryComparer(dictionary));
        var changed = false;
        foreach (var (key, value) in dictionary)
        {
            var entry = NormalizeValue(value, visited);
            normalized[key] = entry.Value;
            changed |= entry.Changed;
        }

        return changed
            ? new(normalized, Changed: true)
            : new(dictionary, Changed: false);
    }

    private static IEqualityComparer<string> DictionaryComparer(IReadOnlyDictionary<string, object?> dictionary) =>
        dictionary is Dictionary<string, object?> concrete
            ? concrete.Comparer
            : StringComparer.Ordinal;

    private static NormalizedValue NormalizeDictionaryValue(IDictionary dictionary, HashSet<object> visited)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        var changed = false;

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                return new(dictionary, Changed: false);
            }

            var value = NormalizeValue(entry.Value, visited);
            normalized[key] = value.Value;
            changed |= value.Changed;
        }

        return changed
            ? new(normalized, Changed: true)
            : new(dictionary, Changed: false);
    }

    private static NormalizedValue NormalizeEnumerableValue(IEnumerable enumerable, Type type, HashSet<object> visited)
    {
        if (type == typeof(string))
        {
            return new(enumerable, Changed: false);
        }

        var values = new List<object?>();
        var changed = false;
        foreach (var item in enumerable)
        {
            var value = NormalizeValue(item, visited);
            values.Add(value.Value);
            changed |= value.Changed;
        }

        if (!changed)
        {
            return new(enumerable, Changed: false);
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType() ?? typeof(object);
            if (values.All(value => CanAssign(value, elementType)))
            {
                var array = Array.CreateInstance(elementType, values.Count);
                for (var i = 0; i < values.Count; i++)
                {
                    array.SetValue(values[i], i);
                }

                return new(array, Changed: true);
            }

            return new(values.ToArray(), Changed: true);
        }

        var listElementType = EnumerableElementType(type);
        if (listElementType is not null && values.All(value => CanAssign(value, listElementType)))
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(listElementType))!;
            foreach (var value in values)
            {
                list.Add(value);
            }

            return new(list, Changed: true);
        }

        return new(values, Changed: true);
    }

    private static NormalizedValue NormalizeDigitalBrainObject(object value, Type type, HashSet<object> visited)
    {
        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();
        if (constructor is null)
        {
            return new(value, Changed: false);
        }

        var arguments = new object?[constructor.GetParameters().Length];
        var changed = false;
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        var parameters = constructor.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!properties.TryGetValue(parameters[i].Name ?? string.Empty, out var property))
            {
                return new(value, Changed: false);
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (TargetInvocationException)
            {
                return new(value, Changed: false);
            }

            var normalized = NormalizeValue(propertyValue, visited);
            if (!CanAssign(normalized.Value, parameters[i].ParameterType))
            {
                return new(value, Changed: false);
            }

            arguments[i] = normalized.Value;
            changed |= normalized.Changed;
        }

        if (!changed)
        {
            return new(value, Changed: false);
        }

        return new(constructor.Invoke(arguments), Changed: true);
    }

    private static Type? EnumerableElementType(Type type)
    {
        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
        {
            return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
            .Where(candidate => candidate.IsGenericType)
            .FirstOrDefault(candidate => candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static bool CanAssign(object? value, Type type) =>
        value is null
            ? !type.IsValueType || Nullable.GetUnderlyingType(type) is not null
            : type.IsInstanceOfType(value);

    private static object? UnwrapJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => UnwrapJsonElement(property.Value),
            StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(UnwrapJsonElement).ToArray(),
        _ => element.GetString()
    };

    private readonly record struct NormalizedValue(object? Value, bool Changed);

    public static class NeuronInstrumentation
    {
        public static readonly ActivitySource Source = new("DigitalBrain.Neuron");
        public static readonly Meter Meter = new("DigitalBrain.Neuron");
        public static readonly Counter<long> SynapsesIn = Meter.CreateCounter<long>("db.synapses.in");
        public static readonly Counter<long> SynapsesOut = Meter.CreateCounter<long>("db.synapses.out");
        public static readonly Histogram<double> HandleDuration = Meter.CreateHistogram<double>("db.handle.duration");
    }
}
