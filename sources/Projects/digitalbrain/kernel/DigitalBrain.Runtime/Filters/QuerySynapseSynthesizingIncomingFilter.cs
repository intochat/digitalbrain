using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Streams;

namespace DigitalBrain.Runtime.Filters;

// Resolves IGrainContextAccessor and IClusterClient lazily via IServiceProvider
// so the DI validator does not detect a static cycle (filter ->
// IGrainContextAccessor -> HostedClient -> IGrainReferenceRuntime ->
// IEnumerable<IIncomingGrainCallFilter> -> filter). At runtime Orleans has
// these services ready before any filter is invoked.
public sealed class QuerySynapseSynthesizingIncomingFilter(IServiceProvider services)
    : IIncomingGrainCallFilter
{
    IGrainContextAccessor? _grainContextAccessor;
    IClusterClient? _clusterClient;

    IGrainContextAccessor GrainContextAccessor =>
        _grainContextAccessor ??= services.GetRequiredService<IGrainContextAccessor>();

    IClusterClient ClusterClient =>
        _clusterClient ??= services.GetRequiredService<IClusterClient>();

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        await context.Invoke();

        if (context.Grain is not INeuron and not INeuronWithStringKey) return;

        var declaring = context.InterfaceMethod.DeclaringType;
        if (declaring == typeof(INeuron) || declaring == typeof(INeuronWithStringKey)) return;

        var grainContext = GrainContextAccessor.GrainContext;
        if (grainContext is null) return;

        var receiverType = grainContext.GrainInstance?.GetType().Name
            ?? grainContext.GrainId.Type.ToString()
            ?? "";
        var receiverId = ResolveKeyAsGuid(grainContext.GrainId);

        var query = new QuerySynapse(Method: context.InterfaceMethod.Name,
        ReturnTypeName: context.InterfaceMethod.ReturnType.Name) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: ResolveCorrelationId(),
            causationId: null,
            callerNeuronId: ResolveCallerId(),
            callerNeuronType: RequestContext.Get(CallerStampingOutgoingFilter.CallerNeuronTypeKey)?.ToString(),
            receiverNeuronId: receiverId,
            receiverNeuronType: receiverType,
            timestamp: TimeProvider.System.GetUtcNow()
        ) };

        var streamProvider = ClusterClient.GetStreamProvider(Neuron.SynapseStreamProvider);
        var timeline = streamProvider.GetStream<Synapse>(
            StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));
        await timeline.OnNextAsync(query);
    }

    static Guid ResolveKeyAsGuid(GrainId grainId)
    {
        var keyStr = grainId.Key.ToString() ?? "";
        if (Guid.TryParse(keyStr, out var guid)) return guid;
        return StreamKeys.StringKeyToGuid(keyStr);
    }

    static Guid ResolveCorrelationId() => RequestContext.Get(CallerStampingOutgoingFilter.CorrelationIdKey) switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var parsed) => parsed,
        _ => Guid.NewGuid()
    };

    static Guid ResolveCallerId() => RequestContext.Get(CallerStampingOutgoingFilter.CallerNeuronIdKey) switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var parsed) => parsed,
        _ => Guid.Empty
    };
}
