using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.UI;

namespace DigitalBrain;

/// <summary>
/// The current view of a state alias, read and activated at that alias's state neuron (<c>{brain}/{alias}/default</c>).
/// A view synapse returning <see cref="View"/> stores it there with a generation that increases on every stored view.
/// </summary>
[NeuronAlias("synapse.view")]
public interface IViewReader
{
    /// <summary>The current view of <paramref name="alias"/>, or null before the first view (or when the neuron is not that alias's).</summary>
    /// <param name="alias">The state alias.</param>
    [ReadOnlyCall]
    Task<PublishedView?> Read(string alias);

    /// <summary>
    /// Activates the node keyed <paramref name="nodeKey"/> of the view of <paramref name="generation"/>: journals its <see cref="Emit"/>'s
    /// signal (payload its args, plus <c>value</c> when <paramref name="fieldValue"/> is given) and runs the alias's reactions on it.
    /// The caller is the signal's cause.
    /// </summary>
    /// <param name="alias">The state alias that owns the view.</param>
    /// <param name="generation">The generation of the view the caller saw.</param>
    /// <param name="nodeKey">The key of the activated node.</param>
    /// <param name="fieldValue">The field value, if the node is a field.</param>
    /// <exception cref="ActivationRefusedException">The view is stale, the node is unknown or emits nothing, or its emit names another owner.</exception>
    Task Activate(string alias, long generation, string nodeKey, string? fieldValue);
}

/// <summary>The stored view of a state alias.</summary>
/// <param name="Alias">The state alias.</param>
/// <param name="Generation">The generation, increasing with every stored view.</param>
/// <param name="View">The view.</param>
public sealed record PublishedView(string Alias, long Generation, View View);

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

    /// <summary>Creates the exception for <paramref name="reason"/>.</summary>
    /// <param name="reason">One of the reason constants.</param>
    public ActivationRefusedException(string reason) : base($"The activation was refused: {reason}.") => Reason = reason;

    /// <summary>Creates the exception.</summary>
    public ActivationRefusedException() : this(UnknownNode) { }

    /// <summary>Creates the exception with a message and cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The cause.</param>
    public ActivationRefusedException(string message, Exception inner) : base(message, inner) => Reason = UnknownNode;

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
