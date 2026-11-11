namespace DigitalBrain.Units;

/// <summary>A generated source went live for the brain (new, or a changed build).</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The build hash now live.</param>
/// <param name="Aliases">The state aliases it declares.</param>
[Signal("synapse.deployed")]
public sealed record Deployed(string SourceKey, string Hash, IReadOnlyList<string> Aliases) : Signal;

/// <summary>A new build of a generated source is staged until its changed installs are approved.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The staged build hash.</param>
[Signal("synapse.staged")]
public sealed record Staged(string SourceKey, string Hash) : Signal;

/// <summary>A generated source was retired from the brain.</summary>
/// <param name="SourceKey">The source key.</param>
[Signal("synapse.undeployed")]
public sealed record Undeployed(string SourceKey) : Signal;
