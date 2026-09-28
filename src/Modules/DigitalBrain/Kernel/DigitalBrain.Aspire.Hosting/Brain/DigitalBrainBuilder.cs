using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainBuilder
{
    private readonly BrainCompositionBuilder _composition = new();
    private bool _hasDeclarations;
    private bool _materialized;
    private readonly Dictionary<Type, IConfiguration> _compiledConfiguration = [];

    internal void SetModuleConfiguration(ModuleDefinition module)
        => _compiledConfiguration[module.ModuleType] = new ConfigurationBuilder().AddInMemoryCollection(module.Configuration).Build();

    public IConfiguration GetModuleConfiguration<TModule>()
        => _compiledConfiguration.TryGetValue(typeof(TModule), out var configuration) ? configuration : ApplicationBuilder.Configuration;

    public DigitalBrainBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        _composition.WithModule(configure);
        _hasDeclarations = true;
        return this;
    }

    public DigitalBrainBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        _composition.ConfigureModule(configure);
        return this;
    }

    internal void Materialize()
    {
        if (_materialized) { return; }
        if (_hasDeclarations && ApplicationBuilder.Configuration[CompositionOverrideTransport.ConfigurationKey] is { } overrides)
        {
            if (!ApplicationBuilder.Configuration.GetValue<bool>("DigitalBrain:Testing:Enabled"))
            { throw new InvalidOperationException("Composition overrides require an explicitly enabled test deployment."); }
            _composition.ApplyOverrides(overrides);
        }
        var composition = _composition.Build();
        if (composition.RequiresLocalServices)
        { throw new NotSupportedException("Local service substitutions are only supported by in-process tests."); }
        _materialized = true;
        if (_hasDeclarations) { this.AddModules(composition.Modules); }
    }

    private readonly List<Type> _modules = [];
    private readonly List<DigitalBrainModuleProjection> _projections = [];
    private readonly List<IResource> _startupDependencies = [];
    private readonly Dictionary<Type, object> _states = [];

    private readonly Dictionary<string, IResourceBuilder<DigitalBrainModuleResource>> _moduleNodes = new(StringComparer.Ordinal);

    internal DigitalBrainBuilder(
        IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<DigitalBrainResource> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        ApplicationBuilder = builder;
        Name = name;
        Resource = resource;
        Orleans = null!;
        GrainState = null!;
    }

    internal void AttachRuntime(
        OrleansService orleans,
        IResourceBuilder<AzureBlobStorageResource> grainState)
    {
        ArgumentNullException.ThrowIfNull(orleans);
        ArgumentNullException.ThrowIfNull(grainState);
        Orleans = orleans;
        GrainState = grainState;
    }

    public string Name { get; }

    public IDistributedApplicationBuilder ApplicationBuilder { get; }

    public IResourceBuilder<DigitalBrainResource> Resource { get; }

    internal IResourceBuilder<AzureBlobStorageResource> GrainState { get; private set; }

    internal OrleansService Orleans { get; private set; }

    internal IReadOnlyList<DigitalBrainModuleProjection> Projections => _projections;

    internal IReadOnlyList<IResource> StartupDependencies => _startupDependencies;

    internal IReadOnlyList<Type> Modules => _modules;

    internal void AddModule(Type module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (!_modules.Contains(module))
        {
            _modules.Add(module);
            Resource.WithAnnotation(new BrainModuleAnnotation(module));
        }
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

    public void AddProjection(DigitalBrainModuleProjection projection)
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

    public DigitalBrainClientReference AsClient() => new(this);

    internal bool HasResource(string name)
        => ApplicationBuilder.Resources.Any(resource =>
            string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase));

    public IResourceBuilder<DigitalBrainModuleResource> GetOrAddModuleNode(Type moduleType)
        => GetOrAddModuleNode(DigitalBrainHostingNames.ForModule(moduleType));

    public IResourceBuilder<DigitalBrainModuleResource> GetOrAddModuleNode(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (_moduleNodes.TryGetValue(displayName, out var existing))
        {
            return existing;
        }

        var node = ApplicationBuilder.AddResource(new DigitalBrainModuleResource(displayName))
            .ExcludeFromManifest();
        node.WithParentRelationship(Resource);
        node.WithInitialState(new CustomResourceSnapshot
        {
            ResourceType = "Module",
            CreationTimeStamp = DateTime.UtcNow,
            State = KnownResourceStates.Running,
            Properties = [new(CustomResourceKnownProperties.Source, displayName)],
        });
        _moduleNodes.Add(displayName, node);
        return node;
    }
}
