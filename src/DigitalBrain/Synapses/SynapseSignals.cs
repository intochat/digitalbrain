namespace DigitalBrain;

/// <summary>A state alias stored a new view; journaled, read it with <see cref="IViewReader.Read"/>.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="Generation">The generation of the stored view.</param>
[Signal("synapse.view-changed")]
public sealed record ViewChanged(string Alias, long Generation) : Signal;

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

/// <summary>A reaction or view synapse of a state neuron threw. Journaled once, by the state neuron, in place of the change it did not make.</summary>
/// <param name="Alias">The state alias (the unit).</param>
/// <param name="Function">The synapse function name.</param>
/// <param name="Cause">The alias of the arriving signal; the state alias for a view.</param>
/// <param name="CauseId">The <see cref="Causation.Address"/> (publisher:sequence) of the arriving signal, or null when it was not journaled.</param>
/// <param name="Error">The exception type name.</param>
/// <param name="Message">The exception message, at most 500 characters.</param>
[Signal("synapse.failed")]
public sealed record SynapseFailed(string Alias, string Function, string Cause, string? CauseId, string Error, string Message) : Signal;

/// <summary>A state neuron saved a new state; durable, because a state neuron publishes what it did, not what it heard.</summary>
/// <param name="Neuron">The neuron key.</param>
/// <param name="Cause">The <c>[Signal]</c> alias (or type name) of the signal that caused the change.</param>
[Signal("synapse.state-changed")]
public sealed record StateChanged(string Neuron, string Cause) : Signal;
