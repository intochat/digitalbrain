using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime;

public sealed class GrainRegistry
{
    private readonly IGrainFactory _grainFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GrainRegistry> _logger;

    public GrainRegistry(IGrainFactory grainFactory, IServiceProvider serviceProvider, ILogger<GrainRegistry> logger)
    {
        _grainFactory = grainFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IGrain Resolve(string fqn, string primaryKey, Type targetInterface)
    {
        bool exists = false;

        // 1. Check IInterpretedNeuronRegistry
        var registry = _serviceProvider.GetService<IInterpretedNeuronRegistry>();
        if (registry != null)
        {
            if (registry.TryGet(fqn, out _))
            {
                exists = true;
            }
        }

        // 2. Check static types in AppDomain
        if (!exists)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetType(fqn) != null)
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            var grainId = GrainId.Create(GrainType.Create(fqn), primaryKey);
            var method = typeof(IGrainFactory).GetMethods()
                .First(m => m.Name == nameof(IGrainFactory.GetGrain) && 
                            m.IsGenericMethod && 
                            m.GetParameters().Length == 1 && 
                            m.GetParameters()[0].ParameterType == typeof(GrainId));
            return (IGrain)method.MakeGenericMethod(targetInterface).Invoke(_grainFactory, new object[] { grainId })!;
        }

        _logger.LogWarning("FQN '{Fqn}' is unresolved. Emitting NeuronUnresolvedReference and returning placeholder stub grain.", fqn);

        // Emit NeuronUnresolvedReference synapse in the background
        _ = Task.Run(async () =>
        {
            try
            {
                var client = _serviceProvider.GetRequiredService<IClusterClient>();
                var streamProvider = client.GetStreamProvider(Neuron.SynapseStreamProvider);
                var timelineStream = streamProvider.GetStream<Synapse>(
                    StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));
                
                var unresolved = new NeuronUnresolvedReference(
                    NeuronType: BrainScopeHelper.GetActiveScope() ?? "System",
                    TargetReference: fqn
                );

                var metadata = SynapseMetadata.Create(
                    synapseId: Guid.NewGuid(),
                    correlationId: Guid.NewGuid(),
                    causationId: null,
                    callerNeuronId: Guid.Empty,
                    callerNeuronType: "System",
                    receiverNeuronId: Guid.Empty,
                    receiverNeuronType: "System",
                    timestamp: DateTimeOffset.UtcNow
                );

                typeof(Synapse).GetProperty("Headers")?.SetValue(unresolved, metadata);

                await timelineStream.OnNextAsync(unresolved);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to emit NeuronUnresolvedReference for {fqn}: {ex.Message}");
            }
        });

        // Return placeholder stub grain
        var placeholderGrainId = GrainId.Create(
            GrainType.Create("DigitalBrain.Runtime.UnresolvedNeuronPlaceholderGrain"), 
            primaryKey);
        var placeholderMethod = typeof(IGrainFactory).GetMethods()
            .First(m => m.Name == nameof(IGrainFactory.GetGrain) && 
                        m.IsGenericMethod && 
                        m.GetParameters().Length == 1 && 
                        m.GetParameters()[0].ParameterType == typeof(GrainId));
        return (IGrain)placeholderMethod.MakeGenericMethod(targetInterface).Invoke(_grainFactory, new object[] { placeholderGrainId })!;
    }
}

[GrainType("DigitalBrain.Runtime.UnresolvedNeuronPlaceholderGrain")]
public sealed class UnresolvedNeuronPlaceholderGrain
    : DurableGrain, ICallNeuronTarget, IStreamNeuronTarget, IResourceNeuronTarget, IPredicateNeuronTarget
{
    public Task<string> AskAsync(string prompt)
    {
        return Task.FromResult("{\"unresolved\": true}");
    }

    public async IAsyncEnumerable<string> StreamAsync(string prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return "{\"unresolved\": true}";
        await Task.CompletedTask;
    }

    public Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        return Task.FromResult<string?>(null);
    }

    public Task WriteAsync(string key, string value, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        return Task.FromResult(false);
    }
}
