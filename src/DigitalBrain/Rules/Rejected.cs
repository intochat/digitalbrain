namespace DigitalBrain.Rules;

/// <summary>
/// An operation was refused because rules were broken. <c>Neuron.Check</c> publishes it ephemerally: cluster listeners hear it,
/// synapse <c>[On]</c> subscribers do not, and it is never journaled.
/// </summary>
/// <param name="Operation">The refused operation.</param>
/// <param name="Rules">The names of the broken rules, in evaluation order.</param>
/// <param name="Messages">The messages of the broken rules, in the same order.</param>
[Signal("rule.rejected")]
public sealed record Rejected(string Operation, string[] Rules, string[] Messages) : Signal;
