using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainBuilder
{
    private readonly List<DigitalBrainModuleProjection> _projections = [];
    private readonly List<IResource> _startupDependencies = [];
    private readonly Dictionary<Type, object> _state = [];

    internal DigitalBrainBuilder(
        IDistributedApplicationBuilder applicationBuilder,
        string name,
        IResourceBuilder<DigitalBrainResource> resource,
        OrleansService orleans,
        IResourceBuilder<AzureBlobStorageResource> grainState)
    {
        ApplicationBuilder = applicationBuilder;
        Name = name;
        Resource = resource;
        Orleans = orleans;
        GrainState = grainState;
    }

    public IDistributedApplicationBuilder ApplicationBuilder { get; }

    public string Name { get; }

    public IResourceBuilder<DigitalBrainResource> Resource { get; }

    internal OrleansService Orleans { get; }

    internal IResourceBuilder<AzureBlobStorageResource> GrainState { get; }

    internal IReadOnlyList<DigitalBrainModuleProjection> Projections => _projections;

    internal IReadOnlyList<IResource> StartupDependencies => _startupDependencies;

    public DigitalBrainClientReference AsClient() => new(this);

    public TState GetOrAddState<TState>(Func<DigitalBrainBuilder, TState> factory, out bool added)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (_state.TryGetValue(typeof(TState), out var existing))
        {
            added = false;
            return (TState)existing;
        }

        var state = factory(this)
            ?? throw new InvalidOperationException($"State factory for '{typeof(TState).Name}' returned null.");
        _state.Add(typeof(TState), state);
        added = true;
        return state;
    }

    internal void AddProjection(DigitalBrainModuleProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (_projections.Any(existing => existing.GetType() == projection.GetType()))
        {
            throw new InvalidOperationException(
                $"{projection.GetType().Name} is already configured on brain '{Name}'. Add it exactly once.");
        }

        _projections.Add(projection);
    }

    internal void RequireHealthyBeforeStart(IResource dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        if (!_startupDependencies.Contains(dependency))
        {
            _startupDependencies.Add(dependency);
        }
    }
}
