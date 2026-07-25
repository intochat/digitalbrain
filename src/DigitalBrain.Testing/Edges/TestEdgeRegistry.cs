using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

internal sealed class TestEdgeRegistry
{
    private readonly Dictionary<TestEdgeKind, EdgeRegistration> _adapters = [];
    private readonly MethodScopedConfigurationProvider _configuration = new();
    private readonly MethodScopedConfigurationSource _configurationSource;
    private readonly Lock _gate = new();
    private TimeProvider? _timeProvider;
    private Action? _timeReset;
    private long _methodGeneration;
    private bool _sealed;

    internal TestEdgeRegistry()
        => _configurationSource = new(_configuration);

    internal void ConfigureChatClient<TService, TScript>(
        IReadOnlyCollection<Type> neuronAliases,
        TService adapter,
        TScript script,
        Action<TScript> reset)
        where TService : class
        where TScript : class
    {
        ArgumentNullException.ThrowIfNull(neuronAliases);
        var aliases = neuronAliases
            .Where(alias => alias is { IsClass: true, IsAbstract: false })
            .Distinct()
            .ToArray();
        if (aliases.Length == 0 || aliases.Length != neuronAliases.Count)
        {
            throw new ArgumentException(
                "A chat-client edge requires one or more distinct concrete neuron alias types.",
                nameof(neuronAliases));
        }

        Configure(
            TestEdgeKind.ChatClient,
            typeof(TService),
            aliases,
            keyed: true,
            adapter,
            script,
            reset);
    }

    internal void ConfigureSouthboundMcpTransport<TService, TScript>(
        TService adapter,
        TScript script,
        Action<TScript> reset)
        where TService : class
        where TScript : class
        => Configure(
            TestEdgeKind.SouthboundMcpTransport,
            typeof(TService),
            serviceKeys: [],
            keyed: false,
            adapter,
            script,
            reset);

    internal void AttachTimeProvider(
        TimeProvider provider,
        Action reset)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(reset);

        lock (_gate)
        {
            if (_timeProvider is not null)
            {
                throw new InvalidOperationException(
                    "The framework-owned TimeProvider test edge is already attached.");
            }

            _timeProvider = provider;
            _timeReset = reset;
        }
    }

    internal void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        EdgeRegistration[] adapters;
        TimeProvider timeProvider;

        lock (_gate)
        {
            adapters = _adapters.Values.ToArray();
            timeProvider = _timeProvider
                ?? throw new InvalidOperationException(
                    "The framework-owned TimeProvider test edge has not been attached.");
        }

        foreach (var registration in adapters)
        {
            if (registration.Keyed)
            {
                foreach (var serviceKey in registration.ServiceKeys)
                {
                    services.AddKeyedSingleton(
                        registration.ServiceType,
                        serviceKey,
                        registration.Adapter);
                }
            }
            else
            {
                services.AddSingleton(
                    registration.ServiceType,
                    registration.Adapter);
            }
        }

        services.AddKeyedSingleton<TimeProvider>(
            NeuronTime.ServiceKey,
            timeProvider);
    }

    internal void ConfigureConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration is not IConfigurationBuilder builder)
        {
            throw new InvalidOperationException(
                "The silo configuration does not support the framework-owned test edge projection.");
        }

        builder.Add(_configurationSource);
    }

    internal long ResetMethodScope()
    {
        Action[] resets;
        Action timeReset;
        long generation;

        lock (_gate)
        {
            _configuration.Clear();
            generation = checked(++_methodGeneration);
            resets = _adapters.Values
                .Select(registration => registration.Reset)
                .ToArray();
            timeReset = _timeReset
                ?? throw new InvalidOperationException(
                    "The framework-owned TimeProvider test edge has not been attached.");
        }

        foreach (var reset in resets)
        {
            reset();
        }

        timeReset();
        return generation;
    }

    internal TScript Script<TScript>(
        TestEdgeKind kind,
        long generation)
        where TScript : class
    {
        lock (_gate)
        {
            EnsureCurrentGeneration(generation);

            if (!_adapters.TryGetValue(kind, out var registration))
            {
                throw new InvalidOperationException(
                    $"The '{kind}' test edge has no assembly-configured adapter.");
            }

            return registration.Script as TScript
                ?? throw new InvalidOperationException(
                    $"The '{kind}' test edge script is '{registration.Script.GetType().FullName}', not '{typeof(TScript).FullName}'.");
        }
    }

    internal void SetOAuthParameter(
        string name,
        string? value,
        long generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            EnsureCurrentGeneration(generation);
            _configuration.Set(name, value);
        }
    }

    internal string? OAuthParameter(
        string name,
        long generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            EnsureCurrentGeneration(generation);
            return _configuration.TryGet(name, out var value)
                ? value
                : null;
        }
    }

    internal void Seal()
    {
        lock (_gate)
        {
            _sealed = true;
        }
    }

    private void Configure<TService, TScript>(
        TestEdgeKind kind,
        Type serviceType,
        IReadOnlyList<Type> serviceKeys,
        bool keyed,
        TService adapter,
        TScript script,
        Action<TScript> reset)
        where TService : class
        where TScript : class
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(reset);

        if (!serviceType.IsInterface
            || !serviceType.IsInstanceOfType(adapter))
        {
            throw new ArgumentException(
                $"The '{kind}' test edge service contract must be an interface.",
                nameof(serviceType));
        }

        lock (_gate)
        {
            if (_sealed)
            {
                throw new InvalidOperationException(
                    "The DigitalBrain test composition is already sealed.");
            }

            if (!_adapters.TryAdd(
                kind,
                new(
                    serviceType,
                    serviceKeys,
                    keyed,
                    adapter,
                    script,
                    () => reset(script))))
            {
                throw new InvalidOperationException(
                    $"The '{kind}' test edge already has an assembly-configured adapter.");
            }
        }
    }

    private void EnsureCurrentGeneration(long generation)
    {
        if (generation <= 0 || generation != _methodGeneration)
        {
            throw new InvalidOperationException(
                "This TestBrain no longer owns the current method-scoped edge state.");
        }
    }

    private sealed record EdgeRegistration(
        Type ServiceType,
        IReadOnlyList<Type> ServiceKeys,
        bool Keyed,
        object Adapter,
        object Script,
        Action Reset);
}
