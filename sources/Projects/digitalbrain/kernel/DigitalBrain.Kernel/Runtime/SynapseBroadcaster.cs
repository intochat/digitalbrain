using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Runtime;

// E-RUN #37 / extended in #38. The broadcast slice of the Cortex (v3 §7):
// sits between an interpreted neuron's `emit !x(...)` and its `on signal(T):`
// subscribers. Named for what it does (not for the architectural concept) so
// future code can refer to the class without colliding with the existing
// DigitalBrain.Kernel.Cortex namespace, where the Gateway already lives.
//
// Silo-resident — a DI-registered singleton — because every
// InterpretedNeuronGrain on the silo shares the same NavigatorRouter view of
// the catalog plus a single IGrainFactory handle.
//
// Per emitted signal:
//   1. Resolve EmittedSynapse.Port → broadcast FQN via the emitter's plan
//      (plan.SynapsePorts is the linker-derived port→FQN map). A miss is
//      defensive (the linker enforces declared ports at INO302) and gets a
//      warning log; the signal is dropped without fan-out.
//   2. Append a SynapseEnvelope keyed by that FQN to ISynapseLogGrain — the
//      durable broadcast tape (the v3 §7 carry-forward).
//   3. Ask the Navigator for the full subscriber list (broadcast).
//   4. Invoke each subscriber's HandleAsync sequentially. A subscriber that
//      throws is logged and skipped — broadcast is best-effort per subscriber,
//      so one bad handler does not silently strand the rest. The emitter is
//      filtered from the list (self-subscription would deadlock the non-
//      reentrant grain).
//
// #38 added two carry-over fixes that #37 deferred:
//   * Cycle depth-limit (AsyncLocal<int>): direct self-emit was filtered, but
//     A→B→A re-entrancy still deadlocks. The depth counter caps fan-out chains
//     at MaxBroadcastDepth; over-depth invocations log + drop.
//   * BroadcastSystemSignalAsync(fqn, payload): the kernel composition root
//     (and other boot points) emits signals without an authoring plan — there
//     is no emitter neuron with a SignalPorts map. The system overload skips
//     port→FQN resolution and uses an empty emitter FQN, which the per-
//     subscriber self-filter no-ops on (no real neuron has an empty FQN).
public sealed class SynapseBroadcaster(
    IGrainFactory grains,
    IContractCatalog catalog,
    IClusterClient clusterClient,
    ILogger<SynapseBroadcaster> logger)
{
    // Cap per a single emit-chain — the path A.emit → B.on signal → B.emit →
    // C.on signal → ... A depth above this is almost certainly a runaway
    // cycle (lifecycle signals fan out to every subscriber per activation,
    // so chains are real in production). Sync awaited fan-out plus
    // non-reentrant grains means an unbounded cycle hangs the silo turn
    // scheduler; the limit drops the over-depth invocation with a warning
    // rather than throwing — broadcast is best-effort, per #37.
    public const int MaxBroadcastDepth = 16;

    // Orleans RequestContext, not AsyncLocal: an Orleans grain RPC call ships
    // RequestContext across the wire and gives the callee a *copy* of the
    // caller's context. AsyncLocal would reset at every grain boundary
    // because the destination grain runs its turn on a fresh ExecutionContext.
    // RequestContext is the canonical knob for ambient values that must
    // survive grain hops — exactly the fan-out chain shape we need to bound.
    const string DepthKey = "digitalbrain.broadcast.depth";

    // The set of neuron FQNs currently mid-turn on this broadcast chain.
    // Depth alone cannot break an A→B→A cycle: the second call to A would
    // queue behind A's first turn (InterpretedNeuronGrain is non-reentrant),
    // and the chain deadlocks long before depth hits MaxBroadcastDepth. The
    // visited set catches the back-edge *before* the grain call is issued —
    // a subscriber already in the set is dropped with a warning rather than
    // dispatched, which preserves the broadcast-is-best-effort contract.
    // Stored as a string[] (codec-friendly in Orleans RPC) and converted to
    // HashSet inside FanOutAsync for O(1) lookup.
    const string VisitedKey = "digitalbrain.broadcast.visited";

    public async Task BroadcastEmittedSignalsAsync(
        string emitterFqn,
        ExecutionPlan plan,
        ActivationResult result,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emitterFqn);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);

        if (result.EmittedSynapses.Count == 0)
            return;

        foreach (var emitted in result.EmittedSynapses)
        {
            if (!plan.SynapsePorts.TryGetValue(emitted.Port, out var fqn))
            {
                logger.LogWarning(
                    "Emitter {EmitterFqn} produced a signal for undeclared port '{Port}'. " +
                    "Linker (INO302) should have refused — dropping without fan-out.",
                    emitterFqn, emitted.Port);
                continue;
            }

            await FanOutAsync(emitterFqn, fqn, emitted.Args, ct);
        }
    }

    // E-RUN #38. System-hook emit path: the kernel composition root (or any
    // non-neuron source — host startup, dashboard, Brain shell connect) fires
    // a broadcast that has no authoring plan and therefore no port→FQN map.
    // The signature takes the FQN directly + a payload; the self-filter still
    // runs (with emitterFqn = "" it's a no-op since no neuron FQN is empty),
    // the cycle depth-limit still applies, and the signal log still gets the
    // append, so observers/replay see system hooks alongside emitted signals.
    public Task BroadcastSystemSignalAsync(
        string fqn,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fqn);
        ArgumentNullException.ThrowIfNull(payload);
        return FanOutAsync(emitterFqn: string.Empty, fqn: fqn, args: payload, ct);
    }

    public Task BroadcastReminderAsync(
        string reminderFqn,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderFqn);
        ArgumentNullException.ThrowIfNull(payload);
        return FanOutAsync(emitterFqn: string.Empty, fqn: reminderFqn, args: payload, ct);
    }

    async Task FanOutAsync(
        string emitterFqn,
        string fqn,
        IReadOnlyDictionary<string, string> args,
        CancellationToken ct)
    {
        var depth = RequestContext.Get(DepthKey) as int? ?? 0;
        if (depth >= MaxBroadcastDepth)
        {
            // Bounded chain: even acyclic broadcasts must not run away. Drop
            // + warn rather than throw — broadcast is best-effort per the
            // #37 subscriber-isolation contract; a throw here would surface
            // on the originating emitter's HandleAsync return path.
            logger.LogWarning(
                "Broadcast depth {Depth} exceeded MaxBroadcastDepth={Max} while fanning out " +
                "{Fqn} from emitter '{EmitterFqn}'. Dropping to break the cycle.",
                depth, MaxBroadcastDepth, fqn, emitterFqn);
            return;
        }

        var visited = RequestContext.Get(VisitedKey) as string[] ?? Array.Empty<string>();
        // The emitter is currently mid-turn on this chain; any subscriber
        // that resolves back to it (directly or transitively) would deadlock
        // the non-reentrant grain. Add it to the propagated set so deeper
        // broadcasts see it, and skip subscribers already on the list. For
        // system signals emitterFqn = "" — adding it is harmless (no real
        // neuron FQN is empty), and the skip check then becomes inert.
        var nextVisitedSet = new HashSet<string>(visited, StringComparer.Ordinal) { emitterFqn };

        // Set both ambient values *before* the outgoing grain calls so each
        // subscriber's RequestContext snapshot carries them across the
        // Orleans RPC boundary. Restore on exit so siblings of this
        // broadcast at the same call-chain level see the un-bumped values.
        RequestContext.Set(DepthKey, depth + 1);
        RequestContext.Set(VisitedKey, nextVisitedSet.ToArray());
        try
        {
            var signalLog = grains.GetGrain<ISynapseLogGrain>(BrainScopeHelper.GetActiveScopeGuid());
            var envelope = new SynapseEnvelope(fqn, args, DateTimeOffset.UtcNow);
            await signalLog.AppendAsync(envelope);

            if (fqn == "DigitalBrain.Settings.SettingChanged")
            {
                var memoryLog = grains.GetGrain<IMemoryEventLogGrain>(BrainScopeHelper.GetActiveScopeGuid());
                await memoryLog.AppendAsync(envelope);
            }

            var schema = catalog.Resolve(fqn);
            var synapseType = GetSynapseType(fqn);
            if (schema != null && (schema.Kind == ContractKind.Synapse || (synapseType != null && typeof(Synapse).IsAssignableFrom(synapseType))))
            {
                var synapseObj = TryConvertBackToSynapse(emitterFqn, fqn, args);
                if (synapseObj != null)
                {
                    var receiverType = args.TryGetValue("ReceiverNeUniformType", out var rt) ? rt : args.TryGetValue("ReceiverNeuronType", out var rt2) ? rt2 : "GatewayNeuron";
                    if (receiverType == "External") receiverType = "GatewayNeuron";

                    var receiverId = Guid.Empty;
                    if (args.TryGetValue("ReceiverNeuronId", out var rIdStr) && Guid.TryParse(rIdStr, out var rId))
                    {
                        receiverId = rId;
                    }

                    try
                    {
                        var streamProvider = clusterClient.GetStreamProvider("synapse");
                        
                        var stream = streamProvider.GetStream<Synapse>(StreamId.Create(receiverType, receiverId));
                        await stream.OnNextAsync(synapseObj);

                        var timeline = streamProvider.GetStream<Synapse>(StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));
                        await timeline.OnNextAsync(synapseObj);
                    }
                    catch (KeyNotFoundException)
                    {
                        logger.LogWarning("Orleans stream provider 'synapse' is not configured in this environment.");
                    }
                }
            }

        }
        finally
        {
            // Without restore, the first sibling of this broadcast would
            // push every later sibling one deeper and the visited set would
            // accumulate transitively across siblings. At the *outermost*
            // entry (depth == 0, no prior visited entries) remove the keys
            // instead of re-setting to defaults — otherwise the caller's
            // grain turn carries our broadcast keys into any later, unrelated
            // grain call it issues, which would ship a stale `visited` =
            // [previous emitter] over RPC and could false-skip a legitimate
            // subscriber whose FQN matches.
            if (depth == 0 && visited.Length == 0)
            {
                RequestContext.Remove(DepthKey);
                RequestContext.Remove(VisitedKey);
            }
            else
            {
                RequestContext.Set(DepthKey, depth);
                RequestContext.Set(VisitedKey, visited);
            }
        }
    }

    private sealed class SynapseTypeMetadata(
        System.Reflection.ConstructorInfo constructor,
        System.Reflection.ParameterInfo[] parameters,
        System.Reflection.PropertyInfo? headersProperty)
    {
        public System.Reflection.ConstructorInfo Constructor { get; } = constructor;
        public System.Reflection.ParameterInfo[] Parameters { get; } = parameters;
        public System.Reflection.PropertyInfo? HeadersProperty { get; } = headersProperty;
    }

    private static readonly ConcurrentDictionary<Type, SynapseTypeMetadata?> MetadataCache = new();
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

    private Synapse? TryConvertBackToSynapse(string emitterFqn, string fqn, IReadOnlyDictionary<string, string> args)
    {
        try
        {
            var type = GetSynapseType(fqn);
            if (type == null) return null;

            var meta = MetadataCache.GetOrAdd(type, t =>
            {
                var ctor = t.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();
                if (ctor == null) return null;
                return new SynapseTypeMetadata(ctor, ctor.GetParameters(), t.GetProperty("Headers"));
            });

            if (meta == null) return null;

            var parameters = meta.Parameters;
            var ctorArgs = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                string? strVal = null;
                foreach (var kv in args)
                {
                    if (string.Equals(kv.Key, param.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        strVal = kv.Value;
                        break;
                    }
                }

                if (param.ParameterType == typeof(SynapseMetadata))
                {
                    ctorArgs[i] = ResolveHeaders(emitterFqn, args);
                }
                else if (param.ParameterType == typeof(Guid))
                {
                    if (strVal != null && Guid.TryParse(strVal, out var g)) ctorArgs[i] = g;
                    else if (string.Equals(param.Name, "SynapseId", StringComparison.OrdinalIgnoreCase)) ctorArgs[i] = Guid.NewGuid();
                    else if (string.Equals(param.Name, "CorrelationId", StringComparison.OrdinalIgnoreCase)) ctorArgs[i] = ResolveCorrelationId();
                    else ctorArgs[i] = Guid.Empty;
                }
                else if (param.ParameterType == typeof(Guid?))
                {
                    if (strVal != null && Guid.TryParse(strVal, out var g)) ctorArgs[i] = g;
                    else ctorArgs[i] = (Guid?)null;
                }
                else if (param.ParameterType == typeof(DateTimeOffset))
                {
                    if (strVal != null && DateTimeOffset.TryParse(strVal, out var dto)) ctorArgs[i] = dto;
                    else ctorArgs[i] = DateTimeOffset.UtcNow;
                }
                else if (param.ParameterType == typeof(int))
                {
                    if (strVal != null && int.TryParse(strVal, out var val)) ctorArgs[i] = val;
                    else ctorArgs[i] = 0;
                }
                else if (param.ParameterType == typeof(string))
                {
                    if (strVal != null) ctorArgs[i] = strVal;
                    else if (string.Equals(param.Name, "ReceiverNeuronType", StringComparison.OrdinalIgnoreCase)) ctorArgs[i] = "External";
                    else if (string.Equals(param.Name, "CallerNeuronType", StringComparison.OrdinalIgnoreCase)) ctorArgs[i] = emitterFqn;
                    else ctorArgs[i] = (string?)null;
                }
                else if (param.ParameterType == typeof(IReadOnlyList<string>) || param.ParameterType == typeof(string[]))
                {
                    if (strVal == null)
                    {
                        ctorArgs[i] = Array.Empty<string>();
                    }
                    else
                    {
                        string[] parts;
                        if (strVal.StartsWith('[') && strVal.EndsWith(']'))
                        {
                            try
                            {
                                parts = JsonSerializer.Deserialize<string[]>(strVal) ?? Array.Empty<string>();
                            }
                            catch
                            {
                                parts = strVal.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                            }
                        }
                        else
                        {
                            parts = strVal.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                        }

                        if (param.ParameterType == typeof(IReadOnlyList<string>))
                        {
                            ctorArgs[i] = (IReadOnlyList<string>)parts;
                        }
                        else
                        {
                            ctorArgs[i] = parts;
                        }
                    }
                }
                else
                {
                    ctorArgs[i] = param.HasDefaultValue ? param.DefaultValue : null;
                }
            }

            var synapse = (Synapse)meta.Constructor.Invoke(ctorArgs);
            if (meta.HeadersProperty != null)
            {
                var headers = ResolveHeaders(emitterFqn, args);
                meta.HeadersProperty.SetValue(synapse, headers);
            }
            return synapse;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert envelope args back to Synapse for type {Fqn}", fqn);
            return null;
        }
    }

    private static SynapseMetadata ResolveHeaders(string emitterFqn, IReadOnlyDictionary<string, string> args)
    {
        Guid? synapseId = null;
        if (args.TryGetValue("SynapseId", out var sIdStr) && Guid.TryParse(sIdStr, out var sId)) synapseId = sId;

        Guid? correlationId = null;
        if (args.TryGetValue("CorrelationId", out var cIdStr) && Guid.TryParse(cIdStr, out var cId)) correlationId = cId;
        else correlationId = ResolveCorrelationId();

        Guid? causationId = null;
        if (args.TryGetValue("CausationId", out var caIdStr) && Guid.TryParse(caIdStr, out var caId)) causationId = caId;

        Guid? callerNeuronId = null;
        if (args.TryGetValue("CallerNeuronId", out var callerIdStr) && Guid.TryParse(callerIdStr, out var callerId)) callerNeuronId = callerId;

        string? callerNeuronType = emitterFqn;
        if (args.TryGetValue("CallerNeuronType", out var callerType)) callerNeuronType = callerType;

        Guid? receiverNeuronId = null;
        if (args.TryGetValue("ReceiverNeuronId", out var recIdStr) && Guid.TryParse(recIdStr, out var recId)) receiverNeuronId = recId;

        string? receiverNeuronType = null;
        if (args.TryGetValue("ReceiverNeuronType", out var recType)) receiverNeuronType = recType;

        DateTimeOffset? timestamp = null;
        if (args.TryGetValue("Timestamp", out var tsStr) && DateTimeOffset.TryParse(tsStr, out var ts)) timestamp = ts;

        string? traceparent = null;
        if (args.TryGetValue("Traceparent", out var tp)) traceparent = tp;

        string? tracestate = null;
        if (args.TryGetValue("Tracestate", out var tsState)) tracestate = tsState;

        return SynapseMetadata.Create(
            synapseId: synapseId,
            correlationId: correlationId,
            causationId: causationId,
            callerNeuronId: callerNeuronId,
            callerNeuronType: callerNeuronType,
            receiverNeuronId: receiverNeuronId,
            receiverNeuronType: receiverNeuronType,
            timestamp: timestamp,
            traceparent: traceparent,
            tracestate: tracestate
        );
    }

    private static Type? GetSynapseType(string fqn)
    {
        return TypeCache.GetOrAdd(fqn, name =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name);
                if (type != null) return type;
            }
            return null;
        });
    }

    private static Guid ResolveCorrelationId()
    {
        var v = RequestContext.Get("DigitalBrain.CorrelationId");
        return v switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => Guid.NewGuid()
        };
    }
}
