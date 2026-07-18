using DigitalBrain.InoLang.Runtime;

namespace DigitalBrain.InoLang.Testing;

public sealed class StubNeuronHost : INeuronHost
{
    public Dictionary<string, string> NeuronReturns { get; } = new(StringComparer.Ordinal);

    // Pinning shape per E-SDK #58: a scenario's `given topic-of(#ask.text) is "Y"`
    // records the canonical "the topic of the subject IS Y" for this scenario.
    // EvaluatePredicateAsync returns true iff the runtime's `target` (the
    // literal on the right of the .ino's `is "..."`) matches the pinned
    // value. The subject argument is ignored — a `given` pins the predicate
    // for any subject under that scenario, matching the InoLang scenario
    // semantics ("in this scenario, the topic is …").
    public Dictionary<string, string> PredicateValues { get; } = new(StringComparer.Ordinal);

    public Task<string> AskAsync(string port, string prompt, CancellationToken ct)
        => Task.FromResult(NeuronReturns.GetValueOrDefault(port, ""));

    public Task<bool> EvaluatePredicateAsync(string builtin, string subject, string target, CancellationToken ct)
        => Task.FromResult(
            PredicateValues.TryGetValue(builtin, out var pinned)
            && string.Equals(pinned, target, StringComparison.Ordinal));
}
