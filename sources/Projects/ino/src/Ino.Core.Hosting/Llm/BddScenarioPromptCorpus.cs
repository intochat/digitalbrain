using Ino.Core;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// <see cref="INeuronPromptCorpus"/> implementation that filters a
/// <see cref="BddScenario"/> list for scenarios tagged with
/// <c>@neuron:&lt;id&gt;</c> and groups them by id.
/// </summary>
public sealed class BddScenarioPromptCorpus : INeuronPromptCorpus
{
    public BddScenarioPromptCorpus(IEnumerable<BddScenario> scenarios)
    {
        var grouped = new Dictionary<NeuronId, List<NeuronPromptPattern>>();
        var total = 0;
        foreach (var scenario in scenarios)
        {
            if (!scenario.TryGetNeuronId(out var neuronId)) continue;
            if (!grouped.TryGetValue(neuronId, out var bucket))
            {
                bucket = new List<NeuronPromptPattern>();
                grouped[neuronId] = bucket;
            }
            bucket.Add(new NeuronPromptPattern(
                NeuronId: neuronId,
                Pattern: scenario.PromptPattern,
                ScenarioName: scenario.ScenarioName,
                SourceFile: scenario.SourceFile));
            total++;
        }

        ByNeuron = grouped.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<NeuronPromptPattern>)kv.Value.ToArray());
        Count = total;
    }

    public IReadOnlyDictionary<NeuronId, IReadOnlyList<NeuronPromptPattern>> ByNeuron { get; }
    public int Count { get; }
}
