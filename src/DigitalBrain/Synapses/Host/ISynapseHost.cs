using DigitalBrain.Contracts;

namespace DigitalBrain;

/// <summary>How a deploy or retire of a synapse source ended, or where a deployed source stands.</summary>
public enum DeployStatus
{
    /// <summary>The source is live: compiled, loaded, activated and installed (installs of brand-new aliases may still wait for the person).</summary>
    Deployed,

    /// <summary>The new version changes the needs of an installed alias: the old version stays live until the person approves the new needs.</summary>
    Staged,

    /// <summary>The source is the same as the live version; nothing changed.</summary>
    Unchanged,

    /// <summary>The source was retired: uninstalled, removed from the catalog and unloaded.</summary>
    Retired,

    /// <summary>The source cannot be deployed or retired (an alias owned by another source, an unknown source key, a refused install).</summary>
    Refused,

    /// <summary>The source did not compile or load; nothing changed.</summary>
    Failed,
}

/// <summary>The result of <see cref="ISynapseHost.Deploy"/> or <see cref="ISynapseHost.Retire"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Hash">The build hash the outcome is about; null when there is none.</param>
/// <param name="Diagnostics">The compiler and analyzer diagnostics; empty for a retire.</param>
/// <param name="Installs">The install outcome per state alias.</param>
/// <param name="Reason">Why it was refused or failed; null otherwise.</param>
public sealed record DeployOutcome(
    DeployStatus Status,
    string? Hash,
    IReadOnlyList<SynapseDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, InstallOutcome> Installs,
    string? Reason);

/// <summary>A synapse source deployed to a brain.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The build hash of the live version.</param>
/// <param name="Aliases">The state aliases the live version declares.</param>
/// <param name="Status"><see cref="DeployStatus.Deployed"/>, or <see cref="DeployStatus.Staged"/> while a new version waits for approval.</param>
/// <param name="StagedHash">The build hash of the staged version; null when none is staged.</param>
/// <param name="At">When the live version was deployed.</param>
public sealed record DeployedSource(string SourceKey, string Hash, IReadOnlyList<string> Aliases, DeployStatus Status, string? StagedHash, DateTimeOffset At);

/// <summary>Deploys, updates and retires generated synapse sources of a brain.</summary>
[NeuronAlias("synapse.host")]
public interface ISynapseHost
{
    /// <summary>
    /// Compiles, loads and makes <paramref name="source"/> live, then installs each alias it declares. A version that changes the needs of
    /// an installed alias is staged until the person approves; the old version stays live meanwhile.
    /// </summary>
    /// <param name="brain">The brain.</param>
    /// <param name="source">The synapse source.</param>
    /// <param name="bindingsPerAlias">Slot bindings per state alias; an alias with no entry binds nothing. When empty, the bindings of <see cref="SynapseSource.Bindings"/> are used; a non-empty argument wins over the source's.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How it ended.</returns>
    Task<DeployOutcome> Deploy(BrainId brain, SynapseSource source, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindingsPerAlias, CancellationToken ct);

    /// <summary>Uninstalls every alias of the source, removes them from the catalog, unloads the source and forgets it.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns><see cref="DeployStatus.Retired"/>, or <see cref="DeployStatus.Refused"/> for an unknown source key.</returns>
    Task<DeployOutcome> Retire(BrainId brain, string sourceKey, CancellationToken ct);

    /// <summary>The sources deployed to <paramref name="brain"/>.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The sources, by source key.</returns>
    [ReadOnlyCall]
    Task<IReadOnlyList<DeployedSource>> List(BrainId brain, CancellationToken ct);
}
