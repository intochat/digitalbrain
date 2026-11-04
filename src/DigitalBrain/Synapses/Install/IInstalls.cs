using DigitalBrain.Contracts;

namespace DigitalBrain;

/// <summary>How an install ended.</summary>
public enum InstallStatus
{
    /// <summary>Approved and recorded for the first time.</summary>
    Installed,

    /// <summary>Approved again because the needs changed.</summary>
    Updated,

    /// <summary>The recorded needs are the same; nothing was asked.</summary>
    Unchanged,

    /// <summary>The person did not approve; nothing was recorded.</summary>
    Denied,

    /// <summary>The request cannot be installed (unknown alias, unbound slot).</summary>
    Refused,
}

/// <summary>A request to install a synapse app.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="Bindings">Slot name to connection key text.</param>
public sealed record InstallRequest(string Alias, IReadOnlyDictionary<string, string> Bindings);

/// <summary>The result of <see cref="IInstalls.Install"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Sentence">The needs sentence; empty when the alias is unknown.</param>
/// <param name="Reason">Why it was refused or denied, or <c>hooks failed: ...</c> when recorded but the host hooks failed; null otherwise.</param>
public sealed record InstallOutcome(InstallStatus Status, string Sentence, string? Reason);

/// <summary>An installed synapse app, as the person approved it.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="NeedsHash">The <see cref="SynapseNeeds.Hash"/> that was approved.</param>
/// <param name="Bindings">Slot name to connection key text.</param>
/// <param name="Schedules">The schedule texts it runs on.</param>
/// <param name="ConsentedAt">When the person approved.</param>
public sealed record InstallRecord(
    string Alias,
    string NeedsHash,
    IReadOnlyDictionary<string, string> Bindings,
    IReadOnlyList<string> Schedules,
    DateTimeOffset ConsentedAt);

/// <summary>The installs of one brain, at <c>{brain}/synapse.installs/default</c>.</summary>
[NeuronAlias("synapse.installs")]
public interface IInstalls
{
    /// <summary>Installs a synapse app, asking the person when its needs are new or changed.</summary>
    /// <param name="request">The alias and its slot bindings.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How it ended.</returns>
    /// <remarks>The call waits for the person, so it may take up to ten minutes.</remarks>
    [CallTimeout(600_000)]
    Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct);

    /// <summary>Retires an installed app; an alias that is not installed is ignored.</summary>
    /// <param name="alias">The state alias.</param>
    /// <param name="ct">Cancellation.</param>
    Task Retire(string alias, CancellationToken ct);

    /// <summary>The record of <paramref name="alias"/>, or null when it is not installed.</summary>
    /// <param name="alias">The state alias.</param>
    /// <param name="ct">Cancellation.</param>
    [ReadOnlyCall]
    Task<InstallRecord?> Describe(string alias, CancellationToken ct);

    /// <summary>Every installed app.</summary>
    /// <param name="ct">Cancellation.</param>
    [ReadOnlyCall]
    Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct);
}
