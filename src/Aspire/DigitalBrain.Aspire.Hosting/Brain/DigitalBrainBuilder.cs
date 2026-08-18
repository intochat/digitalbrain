using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using Aspire.Hosting.Publishing;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainBuilder
{
    private readonly List<DigitalBrainModuleProjection> _projections = [];
    private readonly List<IResource> _startupDependencies = [];
    private readonly Dictionary<Type, object> _states = [];

    internal DigitalBrainBuilder(
        IDistributedApplicationBuilder builder,
        string name,
        OrleansService orleans,
        IResourceBuilder<AzureBlobStorageResource> durableStateStore,
        IResourceBuilder<AzureQueueStorageResource> streams,
        IResourceBuilder<AzureTableStorageResource> pubSub)
    {
        ArgumentNullException.ThrowIfNull(durableStateStore);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(pubSub);

        ApplicationBuilder = builder;
        Name = name;
        Orleans = orleans;
        DurableStateStore = durableStateStore;
        Streams = streams;
        PubSub = pubSub;
    }

    public string Name { get; }

    public IDistributedApplicationBuilder ApplicationBuilder { get; }

    internal IResourceBuilder<AzureBlobStorageResource> DurableStateStore { get; }

    internal IResourceBuilder<AzureQueueStorageResource> Streams { get; }

    internal IResourceBuilder<AzureTableStorageResource> PubSub { get; }

    internal OrleansService Orleans { get; }

    internal IReadOnlyList<DigitalBrainModuleProjection> Projections => _projections;

    internal IReadOnlyList<IResource> StartupDependencies => _startupDependencies;

    internal IResourceBuilder<ParameterResource>? StateProtectionKey { get; private set; }

    public TState GetOrAddState<TState>(Func<DigitalBrainBuilder, TState> create, out bool added)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(create);

        if (_states.TryGetValue(typeof(TState), out var existing))
        {
            added = false;
            return (TState)existing;
        }

        var state = create(this);
        _states.Add(typeof(TState), state);
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

    internal void RequireStateProtection()
    {
        if (StateProtectionKey is not null)
        {
            return;
        }

        var name = $"{Name}-state-protection-key";
        StateProtectionKey = (ApplicationBuilder.ExecutionContext.IsRunMode
                ? ApplicationBuilder.AddParameter(name, new StateProtectionKeyParameterDefault(), secret: true, persist: true)
                : ApplicationBuilder.AddParameter(name, secret: true))
            .WithDescription(
                "Base64-encoded 256-bit key shared by every silo that recovers encrypted durable module state.");
    }

    public ClientDigitalBrainReference AsClient() => new(this);

}
