using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

internal sealed class TestEdgeRegistry
{
    private readonly Lock _gate = new();
    private EdgeRegistration? _chatClient;
    private ServiceEdgeRegistration? _serviceEdge;
    private TimeProvider? _timeProvider;
    private Action? _timeReset;
    private long _methodGeneration;

    internal void ConfigureChatClient<TService, TScript>(
        IReadOnlyCollection<Type> neuronAliases, TService adapter, TScript script, Action<TScript> reset)
        where TService : class
        where TScript : class
    {
        ArgumentNullException.ThrowIfNull(neuronAliases);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(reset);

        var serviceType = typeof(TService);
        if (!serviceType.IsInterface
            || !serviceType.IsInstanceOfType(adapter))
        {
            throw new ArgumentException(
                "The chat-client test edge service contract must be an interface.",
                nameof(adapter));
        }

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

        lock (_gate)
        {
            if (_chatClient is not null)
            {
                throw new InvalidOperationException(
                    "The chat-client test edge already has an assembly-configured adapter.");
            }

            _chatClient = new(serviceType, aliases, adapter, script, () => reset(script));
        }
    }

    internal void ConfigureServiceEdge<TScript>(
        Action<IServiceCollection> configure, TScript script, Action<TScript> reset)
        where TScript : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(reset);

        lock (_gate)
        {
            if (_serviceEdge is not null)
            {
                throw new InvalidOperationException(
                    "A service test edge is already configured for this fixture.");
            }

            _serviceEdge = new(configure, script, () => reset(script));
        }
    }

    internal void AttachTimeProvider(TimeProvider provider, Action reset)
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

        EdgeRegistration? chatClient;
        ServiceEdgeRegistration? serviceEdge;
        TimeProvider timeProvider;

        lock (_gate)
        {
            chatClient = _chatClient;
            serviceEdge = _serviceEdge;
            timeProvider = _timeProvider
                ?? throw new InvalidOperationException(
                    "The framework-owned TimeProvider test edge has not been attached.");
        }

        if (chatClient is not null)
        {
            foreach (var serviceKey in chatClient.ServiceKeys)
            {
                services.AddKeyedSingleton(chatClient.ServiceType, serviceKey, chatClient.Adapter);
            }
        }

        serviceEdge?.Configure(services);

        services.AddKeyedSingleton<TimeProvider>(NeuronTime.ServiceKey, timeProvider);
    }

    internal long ResetMethodScope()
    {
        Action? chatReset;
        Action? serviceReset;
        Action timeReset;
        long generation;

        lock (_gate)
        {
            generation = checked(++_methodGeneration);
            chatReset = _chatClient?.Reset;
            serviceReset = _serviceEdge?.Reset;
            timeReset = _timeReset
                ?? throw new InvalidOperationException(
                    "The framework-owned TimeProvider test edge has not been attached.");
        }

        chatReset?.Invoke();
        serviceReset?.Invoke();
        timeReset();
        return generation;
    }

    internal TScript ChatClientScript<TScript>(long generation)
        where TScript : class
    {
        lock (_gate)
        {
            EnsureCurrentGeneration(generation);

            if (_chatClient is null)
            {
                throw new InvalidOperationException(
                    "The chat-client test edge has no assembly-configured adapter.");
            }

            return _chatClient.Script as TScript
                ?? throw new InvalidOperationException(
                    $"The chat-client test edge script is '{_chatClient.Script.GetType().FullName}', not '{typeof(TScript).FullName}'.");
        }
    }

    internal TScript ServiceEdgeScript<TScript>(long generation)
        where TScript : class
    {
        lock (_gate)
        {
            EnsureCurrentGeneration(generation);

            if (_serviceEdge is null)
            {
                throw new InvalidOperationException(
                    "No service test edge is configured for this fixture.");
            }

            return _serviceEdge.Script as TScript
                ?? throw new InvalidOperationException(
                    $"The service test edge script is '{_serviceEdge.Script.GetType().FullName}', not '{typeof(TScript).FullName}'.");
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
        Type ServiceType, IReadOnlyList<Type> ServiceKeys, object Adapter, object Script, Action Reset);

    private sealed record ServiceEdgeRegistration(
        Action<IServiceCollection> Configure, object Script, Action Reset);
}
