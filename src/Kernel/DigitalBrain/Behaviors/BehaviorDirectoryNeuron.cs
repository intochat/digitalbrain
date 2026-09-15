using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core.Behaviors;

[GrainType("behaviors")]
public sealed class BehaviorDirectoryNeuron : Neuron, IBehaviorDirectory
{
    private readonly IDurableDictionary<string, NeuronId> _entries;

    public BehaviorDirectoryNeuron(NeuronRuntime runtime) : base(runtime)
    {
        _entries = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, NeuronId>>("behavior.index");
    }

    public async Task Register(NeuronId behavior)
    {
        if (behavior.Type != "behavior")
        {
            throw new ArgumentException("Only behavior identities may be indexed.", nameof(behavior));
        }
        if (_entries.ContainsKey(behavior.ToString()))
        {
            return;
        }
        if (_entries.Count >= 4096)
        {
            throw new InvalidOperationException("Behavior index is full (4096 definitions).");
        }
        _entries[behavior.ToString()] = behavior;
        await PersistAsync().ConfigureAwait(true);
    }

    public Task<IReadOnlyList<NeuronId>> List() => Task.FromResult<IReadOnlyList<NeuronId>>([.. _entries.Values]);
}
