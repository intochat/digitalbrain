namespace DigitalBrain.InoLang.Runtime;

// The single boundary through which all non-determinism enters InoLang.
// Production binds these to real grains/SLM; scenarios bind to stubs.
public interface INeuronHost
{
    Task<string> AskAsync(string port, string prompt, CancellationToken ct);

    // E-SDK #58. Bool-returning by design: `where topic-of(x) is "Y"` is a
    // semantic boolean predicate, not a classification-name comparison. The
    // host owns the subject/target pair so an SLM-backed neuron
    // (IPredicateNeuronTarget) can answer directly without the runtime doing a
    // brittle string-equality dance over an LLM's free-form classification.
    // `builtin` selects which neuron handles the predicate (e.g. "topic-of");
    // `subject` is the evaluated argument value; `target` is the literal on
    // the right of `is`.
    Task<bool> EvaluatePredicateAsync(string builtin, string subject, string target, CancellationToken ct);
}
