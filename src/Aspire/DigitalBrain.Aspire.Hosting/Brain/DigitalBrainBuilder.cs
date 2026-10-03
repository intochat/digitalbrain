using DigitalBrain.Contracts;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainBuilder
{
    private readonly Dictionary<string, IConfiguration> _compiledConfiguration = new(StringComparer.Ordinal);
    public IConfiguration GetModuleConfiguration(string id)
        => _compiledConfiguration.TryGetValue(id, out var configuration) ? configuration : ApplicationBuilder.Configuration;

    public DigitalBrainBuilder WithModule(string id, IDigitalBrainModuleHosting? hosting = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        { throw new ArgumentException("Module IDs contain only ASCII letters, digits, and hyphens.", nameof(id)); }
        if (_modules.Contains(id, StringComparer.Ordinal)) { throw new InvalidOperationException($"Module '{id}' is already selected."); }
        if (hosting is not null && hosting.Id != id) { throw new ArgumentException("The hosting adapter belongs to another module.", nameof(hosting)); }
        var replaceOptions = ApplicationBuilder.Configuration.GetValue<bool>($"DigitalBrain:Modules:{id}:ReplaceOptions");
        var values = new Dictionary<string, string?>(replaceOptions ? [] : settings ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ApplicationBuilder.Configuration.GetSection($"DigitalBrain:Modules:{id}:Options").AsEnumerable())
        { if (pair.Value is not null) { values[pair.Key] = pair.Value; } }
        var scopedPrefix = $"DigitalBrain:Brains:{Name}:Modules:{id}:Options";
        foreach (var pair in ApplicationBuilder.Configuration.GetSection(scopedPrefix).AsEnumerable())
        {
            if (pair.Value is not null) { values[$"DigitalBrain:Modules:{id}:Options" + pair.Key[scopedPrefix.Length..]] = pair.Value; }
        }
        _compiledConfiguration[id] = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        _modules.Add(id);
        Resource.WithAnnotation(new BrainModuleAnnotation(id));
        GetOrAddModuleNode(id);
        foreach (var pair in values) { _settings[pair.Key] = pair.Value; }
        hosting?.Configure(this);
        return this;
    }

    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string, string?> Settings => _settings;
    public DigitalBrainBuilder WithModule<THosting>() where THosting : IDigitalBrainModuleHosting, new()
    { var hosting = new THosting(); return WithModule(hosting.Id, hosting); }
    public DigitalBrainBuilder WithModule<THosting, TOptions>(Action<TOptions>? configure = null)
        where THosting : IDigitalBrainModuleHosting, new() where TOptions : class, DigitalBrain.Contracts.IModuleOptions, new()
    {
        var hosting = new THosting();
        var options = new TOptions();
        configure?.Invoke(options);
        options.Validate();
        return WithModule(hosting.Id, hosting, HostingModuleOptions.Flatten(options, hosting.Id));
    }

    private readonly List<string> _modules = [];
    private readonly List<DigitalBrainModuleProjection> _projections = [];
    private readonly List<IResource> _startupDependencies = [];
    private readonly Dictionary<Type, object> _states = [];

    private readonly Dictionary<string, IResourceBuilder<DigitalBrainModuleResource>> _moduleNodes = new(StringComparer.Ordinal);

    internal DigitalBrainBuilder(
        IDistributedApplicationBuilder builder, string name,
        IResourceBuilder<DigitalBrainResource> resource, OrleansService orleans,
        IResourceBuilder<AzureStorageResource>? storage,
        IResourceBuilder<AzureBlobStorageResource>? grainState)
    {
        ApplicationBuilder = builder;
        Name = name;
        Resource = resource;
        Orleans = orleans;
        Storage = storage;
        GrainState = grainState;
    }

    internal bool Dashboard { get; init; }

    public string Name { get; }
    public string ServiceId { get; internal init; } = null!;
    public string ClusterId { get; internal init; } = null!;
    public string? ClusteringResourceName { get; internal init; }

    public string ResourceName(string name) => $"{Name}-{name}";

    public IDistributedApplicationBuilder ApplicationBuilder { get; }

    public IResourceBuilder<DigitalBrainResource> Resource { get; }

    internal IResourceBuilder<AzureBlobStorageResource>? GrainState { get; }

    internal IResourceBuilder<AzureStorageResource>? Storage { get; }

    internal OrleansService Orleans { get; }
    internal IResourceBuilder<AzureTableStorageResource>? Clustering { get; init; }
    internal IResourceBuilder<AzureTableStorageResource>? Reminders { get; init; }

    internal IReadOnlyList<DigitalBrainModuleProjection> Projections => _projections;

    internal IReadOnlyList<IResource> StartupDependencies => _startupDependencies;

    internal IReadOnlyList<string> Modules => _modules;

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

        var node = ApplicationBuilder.AddResource(new DigitalBrainModuleResource(ResourceName(displayName)))
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
