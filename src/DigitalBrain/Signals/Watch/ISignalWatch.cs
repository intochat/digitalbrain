using DigitalBrain.Neurons;
using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Signals.Watch;

/// <summary>
/// A host outside a neuron that watches signals through the session synapse path.
/// Inside a neuron, an <c>[On]</c> reaction and <c>Context.Synapses.Ensure</c> replace this.
/// </summary>
[SuppressMessage("ApiDesign", "RS0026", Justification = "The token-only overload forwards to the subscribed overload; both are the watch contract.")]
public interface ISignalWatch
{
    /// <summary>Signals of kind <typeparamref name="T"/> that <paramref name="neuron"/> publishes.</summary>
    IAsyncEnumerable<T> On<T>(object neuron, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => On<T>(neuron, static () => Task.CompletedTask, cancellationToken);

    /// <summary>The same stream; <paramref name="onSubscribed"/> runs once the session is registered, before queued signals are read.</summary>
    IAsyncEnumerable<T> On<T>(object neuron, Func<Task> onSubscribed, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal;
}
