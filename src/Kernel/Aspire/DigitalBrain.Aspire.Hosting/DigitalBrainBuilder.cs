using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using Aspire.Hosting.Publishing;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainBuilder
{
    private readonly List<ModuleId> _modules = [];
    private readonly List<DigitalBrainModuleProjection> _projections = [];
    private readonly List<IResource> _startupDependencies = [];
    private readonly Dictionary<Type, object> _states = [];

    internal DigitalBrainBuilder(
        IDistributedApplicationBuilder builder,
        string name,
        OrleansService orleans,
        IResourceBuilder<AzureBlobStorageResource> journal,
        IResourceBuilder<AzureQueueStorageResource> streams,
        IResourceBuilder<AzureTableStorageResource> pubSub)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(pubSub);

        ApplicationBuilder = builder;
        Name = name;
        Orleans = orleans;
        Journal = journal;
        Streams = streams;
        PubSub = pubSub;
    }

    public string Name { get; }

    public IDistributedApplicationBuilder ApplicationBuilder { get; }

    internal IResourceBuilder<AzureBlobStorageResource> Journal { get; }

    internal IResourceBuilder<AzureQueueStorageResource> Streams { get; }

    internal IResourceBuilder<AzureTableStorageResource> PubSub { get; }

    internal IReadOnlyList<ModuleId> Modules => _modules;

    internal OrleansService Orleans { get; }

    internal IReadOnlyList<DigitalBrainModuleProjection> Projections => _projections;

    internal IReadOnlyList<IResource> StartupDependencies => _startupDependencies;

    internal IResourceBuilder<ParameterResource>? StateProtectionKey { get; private set; }

    internal string? LocalDevelopmentOAuthCallbackUri { get; private set; }

    internal void UseLocalDevelopmentOAuthCallback(string callbackUri)
    {
        if (LocalDevelopmentOAuthCallbackUri is not null)
        {
            throw new InvalidOperationException(
                $"A local-development OAuth callback is already configured on brain '{Name}'. Configure it exactly once.");
        }

        LocalDevelopmentOAuthCallbackUri = callbackUri;
    }

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

    internal void Select(ModuleId module)
    {
        if (_modules.Contains(module))
        {
            throw new InvalidOperationException($"{module} is already configured on brain '{Name}'. Add each module exactly once.");
        }

        _modules.Add(module);
    }

    public ClientDigitalBrainReference AsClient() => new(this);

    private sealed class StateProtectionKeyParameterDefault : ParameterDefault
    {
        public override string GetDefaultValue()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        public override void WriteToManifest(ManifestPublishingContext context)
            => throw new InvalidOperationException("Local state-protection defaults cannot be published.");
    }
}
