namespace DigitalBrain.Runtime.Runtime;

/// <summary>
/// Binds a named InoLang semantic predicate builtin (e.g. "accepted-version")
/// to a target neuron grain class FQN that implements IPredicateNeuronTarget.
/// </summary>
public sealed record PredicateNeuronBinding(string Builtin, string TargetFqn, string? Key = null);
