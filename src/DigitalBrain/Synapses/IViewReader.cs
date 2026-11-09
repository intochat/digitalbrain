using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain;

/// <summary>
/// The current view of a state alias, read and activated at that alias's state neuron (<c>{brain}/{alias}/default</c>).
/// A view synapse returning a <c>DigitalBrain.UI.View</c> stores it there, as its wire JSON, with a generation that increases on every
/// stored view; the view and its generation are saved with the state, so they survive deactivation and restart.
/// </summary>
[NeuronAlias("synapse.view")]
public interface IViewReader
{
    /// <summary>The current view of <paramref name="alias"/>, or null before the first view (or when the neuron is not that alias's).</summary>
    /// <param name="alias">The state alias.</param>
    [ReadOnlyCall]
    Task<PublishedView?> Read(string alias);

    /// <summary>
    /// Activates the node whose <c>"key"</c> is <paramref name="nodeKey"/> in the view of <paramref name="generation"/>: journals its
    /// <c>"emit"</c>'s signal (payload its <c>args</c>, plus <c>value</c> when <paramref name="fieldValue"/> is given) and runs the alias's
    /// reactions on it. The caller is the signal's cause.
    /// </summary>
    /// <remarks>
    /// The owner's reactions run inline, inside this call, after the signal is journaled: a state neuron never hears its own journal,
    /// so the call returns once the owner has reacted. Other neurons with an alias edge on the signal get it through the journal.
    /// The owner's envelope carries the caller's trace and, as its cause, the address of the journaled emit entry.
    /// Journaling and reacting are not one transaction: a reaction that fails after the emit is journaled behaves like any failing
    /// reaction on a journaled signal, so the emit stands, the state is unchanged, <see cref="SynapseFinished"/> reports
    /// <c>failed</c>, and the failure is thrown to the caller.
    /// </remarks>
    /// <param name="alias">The state alias that owns the view.</param>
    /// <param name="generation">The generation of the view the caller saw.</param>
    /// <param name="nodeKey">The key of the activated node.</param>
    /// <param name="fieldValue">The field value, if the node is a field.</param>
    /// <param name="by">
    /// Who activated the node (an opaque identity, e.g. a person id), set as the payload's reserved <c>"by"</c> member: stamped by the
    /// host; a unit may trust it. Null leaves the payload without <c>"by"</c>. An author can never set it: a view whose emit args declare
    /// <c>"by"</c> is refused at publish.
    /// </param>
    /// <exception cref="ActivationRefusedException">The view is stale, the node is unknown or emits nothing, or its emit names another owner.</exception>
    Task Activate(string alias, long generation, string nodeKey, string? fieldValue, string? by);
}

/// <summary>The stored view of a state alias.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="Generation">The generation, increasing with every stored view.</param>
/// <param name="View">The view as wire JSON (the <c>DigitalBrain.UI</c> shape, nodes discriminated by <c>"kind"</c>).</param>
public sealed record PublishedView(string Alias, long Generation, JsonElement View);

/// <summary>An activation was refused; nothing was journaled.</summary>
public sealed class ActivationRefusedException : InvalidOperationException
{
    /// <summary>The emit names another alias than the view's owner.</summary>
    public const string ForeignOwner = "ForeignOwner";

    /// <summary>The generation is not the current view's (or there is no view yet).</summary>
    public const string Stale = "Stale";

    /// <summary>No node of the view has the key.</summary>
    public const string UnknownNode = "UnknownNode";

    /// <summary>The keyed node emits nothing.</summary>
    public const string NoEmit = "NoEmit";

    /// <summary>No reason was given (the standard exception constructors).</summary>
    public const string Unspecified = "Unspecified";

    /// <summary>Creates the exception for <paramref name="reason"/>.</summary>
    /// <param name="reason">One of the reason constants.</param>
    public ActivationRefusedException(string reason) : base($"The activation was refused: {reason}.") => Reason = reason;

    /// <summary>Creates the exception.</summary>
    public ActivationRefusedException() : this(Unspecified) { }

    /// <summary>Creates the exception with a message and cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The cause.</param>
    public ActivationRefusedException(string message, Exception inner) : base(message, inner) => Reason = Unspecified;

    /// <summary>Why the activation was refused: one of the reason constants.</summary>
    public string Reason { get; }
}

/// <summary>A signal an activated node emitted: journaled under its alias with its JSON payload, delivered to alias reactions only.</summary>
internal sealed record EmittedSignal(string Alias, JsonElement Payload) : Signal, IUntypedSignal
{
    /// <inheritdoc />
    public bool Equals(EmittedSignal? other) => other is not null && string.Equals(Alias, other.Alias, StringComparison.Ordinal)
        && string.Equals(Payload.GetRawText(), other.Payload.GetRawText(), StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Alias);
}
