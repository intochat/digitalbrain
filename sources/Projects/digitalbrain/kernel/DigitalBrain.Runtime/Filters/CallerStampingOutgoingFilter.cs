namespace DigitalBrain.Runtime.Filters;

// Resolves IGrainContextAccessor lazily via IServiceProvider so the DI
// container validator (enabled by Aspire's Development host bootstrap) does
// not see a static cycle through IGrainReferenceRuntime ->
// IEnumerable<IOutgoingGrainCallFilter> -> this filter. The cycle only exists
// in the type graph; at runtime, Orleans resolves filters after the grain
// reference runtime is ready, so a runtime lookup is safe.
public sealed class CallerStampingOutgoingFilter(IServiceProvider services)
    : IOutgoingGrainCallFilter
{
    public const string CallerNeuronIdKey = "DigitalBrain.CallerNeuronId";
    public const string CallerNeuronTypeKey = "DigitalBrain.CallerNeuronType";
    public const string CorrelationIdKey = "DigitalBrain.CorrelationId";
    public const string ExternalCallerSentinel = "External";

    IGrainContextAccessor? _grainContextAccessor;

    IGrainContextAccessor GrainContextAccessor =>
        _grainContextAccessor ??= services.GetRequiredService<IGrainContextAccessor>();

    public Task Invoke(IOutgoingGrainCallContext context)
    {
        var currentGrain = GrainContextAccessor.GrainContext;
        if (currentGrain is not null)
        {
            RequestContext.Set(CallerNeuronIdKey, currentGrain.GrainId.Key.ToString() ?? "");
            RequestContext.Set(
                CallerNeuronTypeKey,
                currentGrain.GrainInstance?.GetType().Name ?? currentGrain.GrainId.Type.ToString() ?? "");
        }
        else
        {
            RequestContext.Set(CallerNeuronTypeKey, ExternalCallerSentinel);
        }

        if (RequestContext.Get(CorrelationIdKey) is null)
            RequestContext.Set(CorrelationIdKey, Guid.NewGuid());

        return context.Invoke();
    }
}
