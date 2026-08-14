using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainBuilder
{
    private readonly List<DigitalBrainModuleProjection> _projections = [];
    private readonly List<IResource> _startupDependencies = [];

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
