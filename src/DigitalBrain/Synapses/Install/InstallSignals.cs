namespace DigitalBrain;

/// <summary>A synapse app was installed with the needs that were approved.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="NeedsHash">The <see cref="SynapseNeeds.Hash"/> of the approved needs.</param>
[Signal("synapse.installed")]
public sealed record Installed(string Alias, string NeedsHash) : Signal;

/// <summary>A synapse app was retired.</summary>
/// <param name="Alias">The state alias.</param>
[Signal("synapse.retired")]
public sealed record Retired(string Alias) : Signal;

/// <summary>The set of installed gates changed. Ephemeral: never journaled.</summary>
/// <param name="Brain">The brain whose gates changed.</param>
[Signal("synapse.gate-changed")]
[EphemeralSignal]
public sealed record GateChanged(string Brain) : Signal;
