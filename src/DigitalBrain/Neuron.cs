using System.Runtime.CompilerServices;

namespace DigitalBrain;

/// <summary>
/// The typed pull form of watching: <c>button.Watch&lt;Clicked&gt;()</c> is the everyday way to
/// receive signals. It is sugar over the one primitive, <see cref="INeuron.Watch"/> — the same
/// synapse, consumed as a stream of the one signal type the watcher cares about.
/// </summary>
public static class Neuron
{
    /// <summary>
    /// Forms a synapse carrying only signals of <typeparamref name="T"/>, pulled as a stream.
    /// No observer to implement: the synapse itself is the receiving endpoint. Disposing it
    /// severs the connection.
    /// </summary>
    public static async Task<ISynapse<T>> Watch<T>(this INeuron neuron) where T : Signal =>
        new TypedSynapse<T>(await neuron.Watch(new PullOnly()));

    private sealed class TypedSynapse<T>(ISynapse synapse) : ISynapse<T> where T : Signal
    {
        public async IAsyncEnumerable<T> Signals(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var signal in synapse.Signals(cancellationToken))
            {
                if (signal is T typed)
                {
                    yield return typed;
                }
            }
        }

        public Task Completion => synapse.Completion;

        public ValueTask DisposeAsync() => synapse.DisposeAsync();
    }

    // One instance per watch, so severing by observer targets exactly one synapse.
    private sealed class PullOnly : INeuronObserver
    {
        public Task OnSignalAsync(Signal signal) => Task.CompletedTask;
    }
}
