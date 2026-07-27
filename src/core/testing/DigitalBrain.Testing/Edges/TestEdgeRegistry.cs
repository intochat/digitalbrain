using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Testing;

internal sealed class TestEdgeRegistry
{
    private readonly Lock _gate = new();
    private EdgeRegistration? _chatClient;
    private McpEdgeRegistration? _mcp;
    private TimeProvider? _timeProvider;
    private Action? _timeReset;
    private long _methodGeneration;

    internal void ConfigureChatClient<TService, TScript>(
        IReadOnlyCollection<Type> neuronAliases,
        TService adapter,
        TScript script,
        Action<TScript> reset)
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

            _chatClient = new(
                serviceType,
                aliases,
                adapter,
                script,
                () => reset(script));
        }
    }

    internal void ConfigureMcpSessionFactory<TScript>(
        IMcpClientSessionFactory factory,
        TScript script,
        Action<TScript> reset)
        where TScript : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(reset);

        lock (_gate)
        {
            if (_mcp is not null)
            {
                throw new InvalidOperationException(
                    "The southbound MCP test edge already has an assembly-configured session factory.");
            }

            _mcp = new(factory, script, () => reset(script));
        }
    }

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

        EdgeRegistration? chatClient;
        McpEdgeRegistration? mcp;
        TimeProvider timeProvider;

        lock (_gate)
        {
            chatClient = _chatClient;
            mcp = _mcp;
            timeProvider = _timeProvider
                ?? throw new InvalidOperationException(
                    "The framework-owned TimeProvider test edge has not been attached.");
        }

        if (chatClient is not null)
        {
            foreach (var serviceKey in chatClient.ServiceKeys)
            {
                services.AddKeyedSingleton(
                    chatClient.ServiceType,
                    serviceKey,
                    chatClient.Adapter);
            }
        }

        if (mcp is not null)
        {
            services.RemoveAll<IMcpClientSessionFactory>();
            services.AddSingleton(mcp.Factory);
        }

        services.AddKeyedSingleton<TimeProvider>(
            NeuronTime.ServiceKey,
            timeProvider);
    }

    internal long ResetMethodScope()
    {
        Action? chatReset;
        Action? mcpReset;
        Action timeReset;
        long generation;

        lock (_gate)
        {
            generation = checked(++_methodGeneration);
            chatReset = _chatClient?.Reset;
            mcpReset = _mcp?.Reset;
            timeReset = _timeReset
                ?? throw new InvalidOperationException(
                    "The framework-owned TimeProvider test edge has not been attached.");
        }

        chatReset?.Invoke();
        mcpReset?.Invoke();
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

    internal TScript McpSessionScript<TScript>(long generation)
        where TScript : class
    {
        lock (_gate)
        {
            EnsureCurrentGeneration(generation);

            if (_mcp is null)
            {
                throw new InvalidOperationException(
                    "The southbound MCP test edge has no assembly-configured session factory.");
            }

            return _mcp.Script as TScript
                ?? throw new InvalidOperationException(
                    $"The southbound MCP test edge script is '{_mcp.Script.GetType().FullName}', not '{typeof(TScript).FullName}'.");
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
        object Adapter,
        object Script,
        Action Reset);

    private sealed record McpEdgeRegistration(
        IMcpClientSessionFactory Factory,
        object Script,
        Action Reset);
}
