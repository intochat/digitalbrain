using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Runtime;
using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public sealed class ProductionNeuronHost : INeuronHost
{
    private readonly IReadOnlyDictionary<string, NeuronBinding> _neurons;
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, NeuronBinding> _predicateBindings;
    private readonly Func<string, string, Task<string>>? _onAsk;

    public ProductionNeuronHost(
        IReadOnlyDictionary<string, NeuronBinding> neurons,
        IServiceProvider serviceProvider,
        IReadOnlyDictionary<string, NeuronBinding>? predicateBindings = null,
        Func<string, string, Task<string>>? onAsk = null)
    {
        _neurons = neurons;
        _serviceProvider = serviceProvider;
        _predicateBindings = predicateBindings ?? new Dictionary<string, NeuronBinding>(StringComparer.Ordinal);
        _onAsk = onAsk;
    }

    public ProductionNeuronHost(
        IReadOnlyDictionary<string, NeuronBinding> neurons,
        IGrainFactory grainFactory,
        IReadOnlyDictionary<string, NeuronBinding>? predicateBindings = null,
        Func<string, string, Task<string>>? onAsk = null)
        : this(neurons, CreateFallbackServiceProvider(grainFactory), predicateBindings, onAsk)
    {
    }

    private static IServiceProvider CreateFallbackServiceProvider(IGrainFactory grainFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(grainFactory);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<GrainRegistry>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<GrainRegistry>.Instance);
        services.AddSingleton<GrainRegistry>();

        services.AddKeyedTransient<ICallNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            string targetFqn;
            string primaryKey;

            if (key is NeuronBinding binding)
            {
                targetFqn = binding.TargetFqn;
                primaryKey = binding.Key ?? binding.TargetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for ICallNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            if (targetFqn == "DigitalBrain.Kernel.Settings.SettingsStore")
            {
                primaryKey = BrainScopeHelper.GetActiveScope();
            }
            else
            {
                primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            }
            return (ICallNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(ICallNeuronTarget));
        });

        services.AddKeyedTransient<IStreamNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            string targetFqn;
            string primaryKey;

            if (key is NeuronBinding binding)
            {
                targetFqn = binding.TargetFqn;
                primaryKey = binding.Key ?? binding.TargetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for IStreamNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            return (IStreamNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(IStreamNeuronTarget));
        });

        services.AddKeyedTransient<IResourceNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            string targetFqn;
            string primaryKey;

            if (key is NeuronBinding binding)
            {
                targetFqn = binding.TargetFqn;
                primaryKey = binding.Key ?? binding.TargetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for IResourceNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            return (IResourceNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(IResourceNeuronTarget));
        });

        services.AddKeyedTransient<IPredicateNeuronTarget>(KeyedService.AnyKey, (sp, key) =>
        {
            string targetFqn;
            string primaryKey;

            if (key is NeuronBinding binding)
            {
                targetFqn = binding.TargetFqn;
                primaryKey = binding.Key ?? binding.TargetFqn;
            }
            else
            {
                targetFqn = key?.ToString() ?? "";
                if (string.IsNullOrEmpty(targetFqn))
                    throw new InvalidOperationException("Key for IPredicateNeuronTarget keyed service cannot be empty.");
                primaryKey = targetFqn;
            }

            primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(primaryKey);
            return (IPredicateNeuronTarget)sp.GetRequiredService<GrainRegistry>().Resolve(targetFqn, primaryKey, typeof(IPredicateNeuronTarget));
        });

        return services.BuildServiceProvider();
    }

    public Task<string> AskAsync(string port, string prompt, CancellationToken ct)
    {
        if (_onAsk is not null)
        {
            return _onAsk(port, prompt);
        }
        var binding = ResolveBinding(port);
        EnsureSigil(binding, PortSigil.Call, port);
        
        var grain = _serviceProvider.GetRequiredKeyedService<ICallNeuronTarget>(binding);
        return grain.AskAsync(prompt);
    }

    public async Task<bool> EvaluatePredicateAsync(string builtin, string subject, string target, CancellationToken ct)
    {
        if (!_predicateBindings.TryGetValue(builtin, out var binding))
            return false;
        EnsureSigil(binding, PortSigil.Predicate, builtin);
        
        var grain = _serviceProvider.GetRequiredKeyedService<IPredicateNeuronTarget>(binding);
        return await grain.EvaluateAsync(subject, target, ct);
    }

    public IAsyncEnumerable<string> StreamAsync(string port, string prompt, CancellationToken ct)
    {
        var binding = ResolveBinding(port);
        EnsureSigil(binding, PortSigil.Stream, port);
        
        var grain = _serviceProvider.GetRequiredKeyedService<IStreamNeuronTarget>(binding);
        return grain.StreamAsync(prompt, ct);
    }

    public Task<string?> ReadResourceAsync(string port, string key, CancellationToken ct)
    {
        var binding = ResolveBinding(port);
        EnsureSigil(binding, PortSigil.Resource, port);
        
        var grain = _serviceProvider.GetRequiredKeyedService<IResourceNeuronTarget>(binding);
        return grain.ReadAsync(key, ct);
    }

    public Task WriteResourceAsync(string port, string key, string value, CancellationToken ct)
    {
        var binding = ResolveBinding(port);
        EnsureSigil(binding, PortSigil.Resource, port);
        
        var grain = _serviceProvider.GetRequiredKeyedService<IResourceNeuronTarget>(binding);
        return grain.WriteAsync(key, value, ct);
    }

    NeuronBinding ResolveBinding(string port)
    {
        if (_neurons.TryGetValue(port, out var binding))
            return binding;
        throw new InvalidOperationException(
            $"No neuron binding for port '{port}'. The interpreter should only " +
            "address ports the linker resolved — a missing binding here means a " +
            "plan/source-of-truth mismatch.");
    }

    static void EnsureSigil(NeuronBinding binding, PortSigil expected, string port)
    {
        if (binding.Sigil == expected)
            return;
        throw new InvalidOperationException(
            $"Neuron binding for port '{port}' has sigil {binding.Sigil}, but the " +
            $"host method requires sigil {expected}. Re-link the .ino so the " +
            "port's `using` declaration matches the host method's contract.");
    }
}
