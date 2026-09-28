using Orleans.Concurrency;

namespace DigitalBrain.AI.Scripted;

// A stand-in model that answers with scripted replies in order and records every prompt it was given.
// Scenarios use it where a real model's answer would make the outcome nondeterministic.
[Alias("ai.llm.scripted"), Orleans.Metadata.DefaultGrainType("ai.llm.scripted")]
public interface IScriptedLLM : ILLM
{
    const string ModelPrefix = "scripted/";

    Task Script(IReadOnlyList<string> replies);
    [ReadOnly, AlwaysInterleave] Task<IReadOnlyList<string>> Prompts();
}
