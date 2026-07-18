namespace DigitalBrain.Runtime.Runtime;

// Sibling of ICallNeuronTarget (E-SDK #45) for SLM-backed semantic predicates
// — the InoLang `where topic-of(#ask.text) is "Car Insurance":` shape (v3
// §4.3 / §4.4). The neuron answers a typed boolean: given a subject (the
// runtime value of the predicate's argument) and a target (the literal on
// the right of `is`), does the predicate hold? Returning Task<bool> rather
// than a string keeps the contract crisp and avoids the LLM-phrasing
// fragility of an ICallNeuronTarget-shaped "answer YES/NO" prompt.
//
// IGrainWithStringKey for the same reason as the other sibling neurons:
// `using $topic = neuron(DigitalBrain.Ai.SlmNeuron["model-key"])` carries the model
// id as a string key, and ProductionNeuronHost defaults the primary key to
// TargetFqn (singleton-per-type) when no key is supplied (Orleans rejects
// empty primary keys).
//
// The CT is carried per Orleans 10.x guidance (cf. #66's per-handler-CT
// carry-over) — same posture as IStreamNeuronTarget / IResourceNeuronTarget.
public interface IPredicateNeuronTarget : IGrainWithStringKey
{
    Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct);
}
