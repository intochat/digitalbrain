namespace DigitalBrain.Units;

/// <summary>A synapse app was installed with the needs that were approved.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="NeedsHash">The <see cref="SynapseNeeds.Hash"/> of the approved needs.</param>
[Signal("synapse.installed")]
public sealed record Installed(string Alias, string NeedsHash) : Signal;

/// <summary>An install was put to the person.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="PlanId">The confirmation plan id.</param>
[Signal("synapse.install-requested")]
public sealed record InstallRequested(string Alias, string PlanId) : Signal;

/// <summary>An install was denied or expired; nothing was recorded.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="PlanId">The confirmation plan id.</param>
/// <param name="Reason">Why it was not approved.</param>
[Signal("synapse.install-denied")]
public sealed record InstallDenied(string Alias, string PlanId, string Reason) : Signal;

/// <summary>A synapse app was retired.</summary>
/// <param name="Alias">The state alias.</param>
[Signal("synapse.retired")]
public sealed record Retired(string Alias) : Signal;
