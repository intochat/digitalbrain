using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.V2.Core.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace DigitalBrain.V2.Core.Runtime;

// The only actor. A grain that receives synapses, dispatches them to IHandle<T> bodies,
// and fires synapses back out — broadcast (Emit) or point-to-point (Ask/Reply). State,
// telemetry, and logging are inherited capsule facets. Kept deliberately minimal: in-memory
// state instead of journaling, timeline-filter routing instead of a subscription registry.
public abstract class Neuron : Grain, INeuron, IAsyncObserver<Synapse>
{
    // The synapse currently being handled, so anything fired inside a handler inherits its
    // correlation/causation and Reply knows who to answer.
    private static readonly AsyncLocal<Synapse?> Incoming = new();

    private const int MaxDepth = 10;
    private const string DepthKey = "db.depth";

    private ILogger? _logger;
    private StreamSubscriptionHandle<Synapse>? _timeline;

    protected ILogger Logger => _logger ??=
        GrainContext.ActivationServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

    protected readonly Dictionary<string, string> State = new();

    // Identity read straight from the Orleans grain id (type + key), so Ask/Reply can
    // re-address the exact grain and no neuron declares its own name.
    protected NeuronId Self
    {
        get
        {
            var id = this.GetGrainId();
            return ToNeuronId(id);
        }
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);

        // Only neurons that handle something subscribe to the timeline; the rest never
        // wake on a broadcast.
        if (HandledTypes(GetType()).Count > 0)
        {
            var stream = this.GetStreamProvider(SynapseStream.ProviderName).Timeline();
            _timeline = await stream.SubscribeAsync(this);
        }
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    // --- receive -----------------------------------------------------------------------

    // Broadcast arriving from the timeline. Filter to the types this neuron actually handles.
    public Task OnNextAsync(Synapse item, StreamSequenceToken? token = null) =>
        HandledTypes(GetType()).Contains(item.GetType()) ? Receive(item) : Task.CompletedTask;

    // Point-to-point arriving directly.
    public Task DeliverAsync(Synapse synapse) => Receive(synapse);

    public Task OnCompletedAsync() => Task.CompletedTask;
    public Task OnErrorAsync(Exception ex)
    {
        Logger.LogError(ex, "Timeline error in {Neuron}", Self);
        return Task.CompletedTask;
    }

    private async Task Receive(Synapse synapse)
    {
        var depth = RequestContext.Get(DepthKey) is int d ? d : 0;
        if (depth > MaxDepth)
        {
            Logger.LogWarning("{Neuron}: depth limit {Max} hit, dropping {Synapse}", Self, MaxDepth, synapse.GetType().Name);
            return;
        }
        RequestContext.Set(DepthKey, depth + 1);

        var previous = Incoming.Value;
        Incoming.Value = synapse;
        try
        {
            await Dispatch(synapse);
        }
        finally
        {
            Incoming.Value = previous;
        }
    }

    // --- fire (one verb, three routings) -----------------------------------------------

    protected Task Emit(Synapse synapse) => Fire(synapse, RoutingMode.Broadcast, receiver: NeuronId.None);

    protected Task Ask(NeuronId target, Synapse synapse) => Fire(synapse, RoutingMode.PointToPoint, target);

    protected Task Ask<TTarget>(string key, Synapse synapse) where TTarget : INeuron
    {
        var target = GrainFactory.GetGrain<TTarget>(key);
        return Fire(synapse, RoutingMode.PointToPoint, ToNeuronId(target.GetGrainId()));
    }

    protected Task Reply(Synapse synapse)
    {
        var caller = Incoming.Value?.Caller ?? NeuronId.None;
        return caller.IsNone ? Task.CompletedTask : Fire(synapse, RoutingMode.PointToPoint, caller);
    }

    private async Task Fire(Synapse synapse, RoutingMode routing, NeuronId receiver)
    {
        var stamped = (synapse.Stamp(Self, routing, Incoming.Value) with { Receiver = receiver });

        if (routing == RoutingMode.Broadcast)
        {
            await this.GetStreamProvider(SynapseStream.ProviderName).Timeline().OnNextAsync(stamped);
        }
        else
        {
            var target = GrainFactory.GetGrain(GrainId.Create(receiver.Type, receiver.Key)).AsReference<INeuron>();
            await target.DeliverAsync(stamped);
        }
    }

    // --- dispatch (cached reflection over IHandle<T> on the concrete type) ---------------

    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<Type, MethodInfo>> HandlerCache = new();

    private Task Dispatch(Synapse synapse)
    {
        var handlers = Handlers(GetType());
        if (handlers.TryGetValue(synapse.GetType(), out var method))
        {
            return (Task)method.Invoke(this, [synapse, CancellationToken.None])!;
        }
        Logger.LogWarning("{Neuron}: no handler for {Synapse}", Self, synapse.GetType().Name);
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<Type, MethodInfo> Handlers(Type neuronType) =>
        HandlerCache.GetOrAdd(neuronType, static t =>
        {
            var map = new Dictionary<Type, MethodInfo>();
            foreach (var i in t.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandle<>)))
            {
                var synapseType = i.GetGenericArguments()[0];
                var method = i.GetMethod(nameof(IHandle<Synapse>.HandleAsync))!;
                map[synapseType] = method;
            }
            return map;
        });

    private static IReadOnlySet<Type> HandledTypes(Type neuronType) => Handlers(neuronType).Keys.ToHashSet();

    private static NeuronId ToNeuronId(GrainId id) => new(id.Type.ToString()!, id.Key.ToString()!);
}
