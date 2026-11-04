namespace DigitalBrain;

/// <summary>A view of a state neuron changed. Ephemeral: never journaled.</summary>
/// <param name="Neuron">The neuron key.</param>
/// <param name="View">The new view value.</param>
[Signal("synapse.view-changed")]
[EphemeralSignal]
public sealed record ViewChanged(string Neuron, object View) : Signal;

/// <summary>A synapse function started. Ephemeral: never journaled.</summary>
/// <param name="Neuron">The neuron key.</param>
/// <param name="Function">The synapse function name.</param>
/// <param name="Signal">The arriving signal alias.</param>
[Signal("synapse.started")]
[EphemeralSignal]
public sealed record SynapseStarted(string Neuron, string Function, string Signal) : Signal;

/// <summary>A synapse function finished. Ephemeral: never journaled.</summary>
/// <param name="Neuron">The neuron key.</param>
/// <param name="Function">The synapse function name.</param>
/// <param name="Outcome">One of <c>allowed</c>, <c>rejected</c>, <c>saved</c>, <c>unchanged</c>, <c>viewed</c>, <c>failed</c>.</param>
/// <param name="ElapsedMs">Elapsed milliseconds.</param>
[Signal("synapse.finished")]
[EphemeralSignal]
public sealed record SynapseFinished(string Neuron, string Function, string Outcome, double ElapsedMs) : Signal;

/// <summary>A state neuron saved a new state; durable, because a state neuron publishes what it did, not what it heard.</summary>
/// <param name="Neuron">The neuron key.</param>
/// <param name="Cause">The <c>[Signal]</c> alias (or type name) of the signal that caused the change.</param>
[Signal("synapse.state-changed")]
public sealed record StateChanged(string Neuron, string Cause) : Signal;
