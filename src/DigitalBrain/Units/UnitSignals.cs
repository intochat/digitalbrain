namespace DigitalBrain.Units;

/// <summary>The waiting plans for a staged build changed.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The staged build hash.</param>
/// <param name="Waiting">The state aliases still waiting for approval.</param>
[Signal("synapse.stage.changed")]
public sealed record StageChanged(string SourceKey, string Hash, IReadOnlyList<string> Waiting) : Signal;

/// <summary>A staged build was superseded or denied and dropped.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The staged build hash.</param>
/// <param name="Reason">Why the stage was dropped.</param>
[Signal("synapse.stage.dropped")]
public sealed record StageDropped(string SourceKey, string Hash, string Reason) : Signal;

/// <summary>A live build was marked as refused and requires rebuild.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The refused build hash.</param>
/// <param name="Reason">Why the build was refused.</param>
[Signal("synapse.build.refused")]
internal sealed record BuildRefused(string SourceKey, string Hash, string Reason) : Signal;

/// <summary>State aliases were claimed by a source key.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Aliases">The claimed aliases.</param>
[Signal("synapse.aliases.claimed")]
public sealed record AliasesClaimed(string SourceKey, IReadOnlyList<string> Aliases) : Signal;

/// <summary>State aliases were released by a source key.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Aliases">The released aliases.</param>
[Signal("synapse.aliases.released")]
public sealed record AliasesReleased(string SourceKey, IReadOnlyList<string> Aliases) : Signal;
