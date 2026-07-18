using Ino.Core;

namespace Ino.Core.Hosting.Llm;

public static class BddScenarioExtensions
{
    const string NeuronTagPrefix = "@neuron:";

    /// <summary>
    /// Reads the <c>@neuron:&lt;id&gt;</c> tag from a scenario's tag list
    /// and returns the parsed <see cref="NeuronId"/>. A scenario may carry
    /// multiple neuron tags (rare — e.g., a generic intent that two
    /// neurons both want to claim); the first one wins. Untagged
    /// scenarios are non-routing — they're reactive narration mocks, etc.
    /// </summary>
    public static bool TryGetNeuronId(this BddScenario scenario, out NeuronId neuronId)
    {
        foreach (var tag in scenario.Tags)
        {
            if (!tag.StartsWith(NeuronTagPrefix, StringComparison.Ordinal)) continue;
            var raw = tag.Substring(NeuronTagPrefix.Length).Trim();
            if (raw.Length == 0) continue;
            neuronId = NeuronId.From(raw);
            return true;
        }
        neuronId = default;
        return false;
    }
}
