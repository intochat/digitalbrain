using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Units;

namespace DigitalBrain;

/// <summary>An action performed against a screen view.</summary>
/// <param name="Alias">The view alias owner (optional; defaults to the screen's own alias).</param>
/// <param name="Generation">The generation of the view seen by the caller.</param>
/// <param name="NodeKey">The unique key of the activated view node.</param>
/// <param name="FieldValue">Optional field value if node is an input field.</param>
public sealed record ScreenAction(string? Alias, long Generation, string NodeKey, string? FieldValue = null)
{
    /// <summary>Creates a screen action without an explicit view alias.</summary>
    public ScreenAction(long generation, string nodeKey, string? fieldValue)
        : this(null, generation, nodeKey, fieldValue)
    {
    }

    /// <summary>Creates a screen action without an explicit view alias or field value.</summary>
    public ScreenAction(long generation, string nodeKey)
        : this(null, generation, nodeKey, null)
    {
    }
}

/// <summary>
/// A screen: an Orleans virtual actor grain that holds domain state and renders a declarative UI View.
/// Interacted with directly via Read, Open, Activate, Act, and Request.
/// </summary>
[NeuronAlias("screen")]
public interface IScreen
{
    /// <summary>Opens the screen, returning its current published view.</summary>
    [ReadOnlyCall]
    Task<PublishedView?> Open();

    /// <summary>Acts on the screen according to the specified action.</summary>
    Task Act(ScreenAction action);

    /// <summary>Reads the current published view of this screen; null if uninitialized or alias mismatches.</summary>
    /// <param name="alias">The view alias (defaults to the screen neuron type).</param>
    [ReadOnlyCall]
    Task<PublishedView?> Read(string? alias = null);

    /// <summary>
    /// Activates a node in the current view: validates generation, nodeKey, and owner; runs reactions inline.
    /// </summary>
    /// <param name="alias">The view alias owner.</param>
    /// <param name="generation">The generation of the view seen by the caller.</param>
    /// <param name="nodeKey">The unique key of the activated view node.</param>
    /// <param name="fieldValue">Optional field value if node is an input field.</param>
    /// <param name="by">Host-asserted identity of the caller; untrusted input cannot forge this.</param>
    /// <exception cref="ActivationRefusedException">Stale generation, unknown node, no emit, or foreign owner.</exception>
    Task Activate(string alias, long generation, string nodeKey, string? fieldValue = null, string? by = null);

    /// <summary>
    /// Dispatches an external signal request to this screen one-way without waiting for reaction completion.
    /// </summary>
    /// <param name="signal">The journal alias of the signal.</param>
    /// <param name="args">Optional JSON payload for the signal constructor/properties.</param>
    [OneWayCall]
    Task Request(string signal, JsonElement? args = null);
}

/// <summary>
/// Extension methods for interacting with screens.
/// </summary>
public static class ScreenExtensions
{
    /// <summary>
    /// Dispatches a typed signal request to this screen one-way without waiting for reaction completion.
    /// </summary>
    /// <param name="screen">The target screen.</param>
    /// <param name="signal">The signal instance.</param>
    public static Task Request<TSignal>(this IScreen screen, TSignal signal) where TSignal : Signal
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(signal);
        return screen.Request(SignalAliases.Of(typeof(TSignal)), JsonSerializer.SerializeToElement(signal, RuntimeProtocol.OptionsFor(typeof(TSignal))));
    }
}
