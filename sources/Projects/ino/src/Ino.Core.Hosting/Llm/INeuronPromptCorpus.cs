using Ino.Core;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Read-only routing corpus derived from <c>Features/*.feature</c> scenarios
/// tagged with <c>@neuron:&lt;id&gt;</c>. Cortex's regex fast-path consumes
/// it: for each candidate neuron, walk its prompt patterns and try them
/// against the inbound user message; a hit short-circuits the LLM classifier.
///
/// Empty under production silos that don't load Bdd scenarios — Cortex falls
/// straight through to the LLM classifier in that case.
/// </summary>
public interface INeuronPromptCorpus
{
    /// <summary>
    /// Pattern entries grouped by neuron id. Each entry is a regex string
    /// (passed straight to <see cref="System.Text.RegularExpressions.Regex"/>
    /// with <c>IgnoreCase</c>) plus the scenario it came from for telemetry.
    /// </summary>
    IReadOnlyDictionary<NeuronId, IReadOnlyList<NeuronPromptPattern>> ByNeuron { get; }

    /// <summary>Total number of routable patterns loaded across all neurons.</summary>
    int Count { get; }
}

/// <param name="NeuronId">Routing target.</param>
/// <param name="Pattern">Regex string from the scenario's <c>Given</c> step.</param>
/// <param name="ScenarioName">For tracing — surfaces the matching scenario in logs/probes.</param>
/// <param name="SourceFile">For tracing — where the scenario was loaded from.</param>
public sealed record NeuronPromptPattern(
    NeuronId NeuronId,
    string Pattern,
    string ScenarioName,
    string SourceFile);
