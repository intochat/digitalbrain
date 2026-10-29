using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Capabilities.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// Resolves capabilities for a brain (R4-02, D-10). Many-connection capabilities come from <see cref="ICapabilityConnections"/>:
/// connected connections whose Provides satisfies the capability, in Settings order; none raises a connection request and returns
/// empty. Single-connection capabilities come from the host's keyed registration under the capability id (<c>secrets@1</c>).
/// </summary>
public sealed class CapabilityBroker : ICapabilityBroker
{
    private readonly CapabilityRegistry _registry;
    private readonly ICapabilityConnections _connections;
    private readonly IServiceProvider _services;
    private readonly ICapabilityBindingInterceptor[] _interceptors;

    /// <summary>Creates the broker for the runtime itself (caller app <c>runtime</c>); use <see cref="ForApp"/> per calling app.</summary>
    public CapabilityBroker(CapabilityRegistry registry, ICapabilityConnections connections, IServiceProvider services, IEnumerable<ICapabilityBindingInterceptor> interceptors)
        : this(registry, connections, services, [.. interceptors ?? throw new ArgumentNullException(nameof(interceptors))], "runtime")
    {
    }

    private CapabilityBroker(CapabilityRegistry registry, ICapabilityConnections connections, IServiceProvider services, ICapabilityBindingInterceptor[] interceptors, string callerApp)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(services);
        _registry = registry;
        _connections = connections;
        _services = services;
        _interceptors = interceptors;
        CallerApp = callerApp;
    }

    /// <summary>The app on whose behalf this broker binds and requests connections.</summary>
    public string CallerApp { get; }

    /// <summary>The same broker acting for <paramref name="callerApp"/>.</summary>
    public CapabilityBroker ForApp(string callerApp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerApp);
        return new CapabilityBroker(_registry, _connections, _services, _interceptors, callerApp);
    }

    /// <inheritdoc />
    public Task<TCapability> Resolve<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class
    {
        var capability = _registry.CapabilityOf(typeof(TCapability));
        if (!_registry.IsSingle(capability))
        {
            throw new InvalidOperationException($"{capability} is a many-connection capability; use ResolveAll.");
        }
        var registered = _services.GetKeyedServices<TCapability>(capability.ToString()).ToList();
        return registered.Count switch
        {
            1 => Task.FromResult(registered[0]),
            0 => throw new InvalidOperationException($"No implementation of {capability} is registered; the host registers one keyed '{capability}'."),
            _ => throw new InvalidOperationException($"{registered.Count} implementations of {capability} are registered; a single-connection capability has exactly one."),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapabilityConnection<TCapability>>> ResolveAll<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class
        => await Bind<TCapability>(brain, _registry.CapabilityOf(typeof(TCapability)), await Satisfying(brain, typeof(TCapability), ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>Every connected storage connection of <paramref name="brain"/> in Settings order; <c>ResolveAll&lt;IStorage&gt;</c>.</summary>
    public Task<IReadOnlyList<CapabilityConnection<IStorage>>> Storages(BrainId brain, CancellationToken ct) => ResolveAll<IStorage>(brain, ct);

    /// <summary>
    /// The storage connection owning <paramref name="item"/>: the connected connection whose <see cref="BrainConnection.Root"/> is the
    /// longest ordinal prefix of the id; with no owning root, every storage connection in Settings order.
    /// </summary>
    public async Task<IReadOnlyList<CapabilityConnection<IStorage>>> ResolveFor(BrainId brain, ItemId item, CancellationToken ct)
    {
        var satisfying = await Satisfying(brain, typeof(IStorage), ct).ConfigureAwait(false);
        var owner = item.Value is null ? null : satisfying
            .Where(connection => !string.IsNullOrEmpty(connection.Root) && item.Value.StartsWith(connection.Root, StringComparison.Ordinal))
            .OrderByDescending(connection => connection.Root!.Length)
            .FirstOrDefault();
        return await Bind<IStorage>(brain, Capability.Storage.V1, owner is null ? satisfying : [owner], ct).ConfigureAwait(false);
    }

    /// <summary>Every single-connection capability registered more than once as a keyed service in <paramref name="services"/>.</summary>
    public static IReadOnlyList<string> SingleRegistrationIssues(CapabilityRegistry registry, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        var issues = new List<string>();
        foreach (var capability in registry.Capabilities.Where(registry.IsSingle).OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            var key = capability.ToString();
            var bindings = registry.BindingsOf(capability);
            var count = services.Count(descriptor => descriptor.IsKeyedService && bindings.Contains(descriptor.ServiceType)
                && key.Equals(descriptor.ServiceKey as string, StringComparison.Ordinal));
            if (count > 1) { issues.Add($"{count} implementations of {capability} are registered; a single-connection capability has exactly one."); }
        }
        return issues;
    }

    private async Task<IReadOnlyList<BrainConnection>> Satisfying(BrainId brain, Type contract, CancellationToken ct)
    {
        var capability = _registry.CapabilityOf(contract);
        var listed = await _connections.ListInOrder(brain, ct).ConfigureAwait(false);
        return [.. listed.Where(connection => connection.Connected && connection.Provides.Any(provided => provided.Satisfies(capability)))];
    }

    private async Task<IReadOnlyList<CapabilityConnection<TCapability>>> Bind<TCapability>(
        BrainId brain, CapabilityId capability, IReadOnlyList<BrainConnection> connections, CancellationToken ct) where TCapability : class
    {
        if (connections.Count == 0)
        {
            await _connections.RequestConnection(brain, capability, CallerApp, ct).ConfigureAwait(false);
            return [];
        }
        var bound = new List<CapabilityConnection<TCapability>>(connections.Count);
        foreach (var connection in connections)
        {
            var implementation = await _connections.Bind<TCapability>(brain, connection.Key, CallerApp, ct).ConfigureAwait(false);
            foreach (var interceptor in _interceptors) { implementation = interceptor.Wrap(brain, connection, CallerApp, implementation); }
            if (implementation is IStorage storage)
            {
                var log = _services.GetService<StorageAccessLog>() ?? new StorageAccessLog();
                implementation = (TCapability)(object)new RecordingStorage(storage, log, CallerApp, brain.Value, connection.Key, connection.Key);
            }
            bound.Add(new CapabilityConnection<TCapability>(connection.Key, connection.Label, implementation));
        }
        return bound;
    }
}
