using System.Text.Json.Serialization;

namespace DigitalBrain.Units;

/// <summary>Identifies a compiled unit build by its source key and content hash.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The build hash.</param>
public readonly record struct BuildRef(string SourceKey, string Hash);

/// <summary>Content-addressed binary payload of a unit build.</summary>
/// <param name="Ref">The build reference.</param>
/// <param name="AssemblyName">The assembly simple name.</param>
/// <param name="Assembly">The compiled assembly bytes.</param>
/// <param name="Symbols">The portable PDB symbol bytes.</param>
public sealed record BuildImage(
    BuildRef Ref,
    string AssemblyName,
    byte[] Assembly,
    byte[] Symbols);

/// <summary>A durable unit build stored in the deployment aggregate.</summary>
/// <param name="Image">The binary image payload.</param>
/// <param name="Files">The C# source files, keyed by relative path.</param>
/// <param name="Icon">The package icon bytes, if any.</param>
/// <param name="Aliases">The state aliases the build declares.</param>
/// <param name="NeedsHashes">The needs hash per state alias.</param>
/// <param name="RebuildReason">Why the build cannot be restored, or null if healthy.</param>
public sealed record StoredBuild(
    BuildImage Image,
    IReadOnlyDictionary<string, string> Files,
    byte[]? Icon,
    IReadOnlyList<string> Aliases,
    IReadOnlyDictionary<string, string> NeedsHashes,
    string? RebuildReason = null)
{
    /// <summary>The build reference.</summary>
    public BuildRef Ref => Image.Ref;
}

/// <summary>A proposed build waiting for user confirmation on one or more aliases.</summary>
/// <param name="Build">The stored build under staging.</param>
/// <param name="Waiting">Mapping of state alias to confirmation plan ID (alias -> planId).</param>
public sealed record StagedBuild(
    StoredBuild Build,
    IReadOnlyDictionary<string, string> Waiting);

/// <summary>Whether to resolve the live build or proposed staged build.</summary>
public enum UnitView
{
    /// <summary>The currently active live build.</summary>
    Live,

    /// <summary>The proposed build under staging, if any; otherwise the live build.</summary>
    Proposed,
}

/// <summary>Lightweight view of deployed source files and metadata without raw binary arrays.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Hash">The build hash.</param>
/// <param name="Files">The source files.</param>
/// <param name="Icon">The icon bytes, if any.</param>
/// <param name="Aliases">The declared state aliases.</param>
/// <param name="DeployedAt">When it went live.</param>
/// <param name="RebuildReason">Why it needs rebuild, if applicable.</param>
public sealed record UnitSourceView(
    string SourceKey,
    string Hash,
    IReadOnlyDictionary<string, string> Files,
    byte[]? Icon,
    IReadOnlyList<string> Aliases,
    DateTimeOffset DeployedAt,
    string? RebuildReason = null);

/// <summary>Pure state of the UnitDeployment aggregate neuron.</summary>
/// <param name="Live">The currently live stored build, or null.</param>
/// <param name="Staged">The currently staged build, or null.</param>
/// <param name="At">When the live build went live.</param>
public sealed record DeploymentState(
    StoredBuild? Live,
    StagedBuild? Staged,
    DateTimeOffset At)
{
    /// <summary>Initial empty deployment state.</summary>
    public DeploymentState() : this(null, null, default) { }

    /// <summary>True when a staged build has no remaining pending asks and is ready to promote to live.</summary>
    public bool ReadyToPromote => Staged is { Waiting.Count: 0 };

    /// <summary>Resolves the build reference for the requested view.</summary>
    public BuildRef? Ref(UnitView view) =>
        view == UnitView.Proposed && Staged is { } staged ? staged.Build.Ref : Live?.Ref;

    /// <summary>Stages a new build proposal with the aliases requiring confirmation.</summary>
    public DeploymentState Propose(StoredBuild build, IEnumerable<string> needsDiff)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(needsDiff);
        var initialWaiting = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in needsDiff)
        {
            initialWaiting[alias] = string.Empty;
        }
        return this with { Staged = new StagedBuild(build, initialWaiting) };
    }

    /// <summary>Updates waiting plan IDs for staged aliases.</summary>
    public DeploymentState Asked(IReadOnlyDictionary<string, string> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        if (Staged is not { } staged) { return this; }
        var waiting = staged.Waiting.Keys
            .Where(plans.ContainsKey)
            .ToDictionary(alias => alias, alias => plans[alias], StringComparer.Ordinal);
        return this with { Staged = staged with { Waiting = waiting } };
    }

    /// <summary>Approves an alias of the staged build if its needs hash matches.</summary>
    public DeploymentState Approve(string alias, string needsHash)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(needsHash);
        if (Staged is null) { return this; }
        if (Staged.Build.NeedsHashes.TryGetValue(alias, out var expected) && !string.Equals(expected, needsHash, StringComparison.Ordinal))
        {
            return this;
        }
        if (!Staged.Waiting.ContainsKey(alias))
        {
            return this;
        }
        var nextWaiting = new Dictionary<string, string>(Staged.Waiting, StringComparer.Ordinal);
        nextWaiting.Remove(alias);
        return this with { Staged = Staged with { Waiting = nextWaiting } };
    }

    /// <summary>Denies a staged install: cancels the staged build if the plan matches.</summary>
    public DeploymentState Deny(string alias, string planId)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(planId);
        if (Staged is { } staged && staged.Waiting.TryGetValue(alias, out var plan) && string.Equals(plan, planId, StringComparison.Ordinal))
        {
            return Supersede();
        }
        return this;
    }

    /// <summary>Cancels and drops the currently staged build.</summary>
    public DeploymentState Supersede() => Staged is null ? this : this with { Staged = null };

    /// <summary>Promotes the staged build to live, clearing the staged build.</summary>
    public DeploymentState Promote(DateTimeOffset now)
    {
        if (Staged is null)
        {
            throw new InvalidOperationException("Cannot promote when there is no staged build.");
        }
        return new DeploymentState(Live: Staged.Build with { RebuildReason = null }, Staged: null, At: now);
    }

    /// <summary>Marks a build as refused or drops it if staged.</summary>
    public DeploymentState Refuse(string hash, string reason)
    {
        ArgumentNullException.ThrowIfNull(hash);
        var state = this;
        if (state.Live is not null && string.Equals(state.Live.Ref.Hash, hash, StringComparison.Ordinal))
        {
            state = state with { Live = state.Live with { RebuildReason = reason } };
        }
        if (state.Staged is not null && string.Equals(state.Staged.Build.Ref.Hash, hash, StringComparison.Ordinal))
        {
            state = state with { Staged = null };
        }
        return state;
    }

    /// <summary>Clears both live and staged builds.</summary>
    public DeploymentState Retire() => Live is null && Staged is null ? this : new();
}
