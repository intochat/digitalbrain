using DigitalBrain.Neurons;

namespace DigitalBrain.Signals.Watch;

// A set of neurons for ISignalWatch.On<T>(Sources) and React<T>(Sources, …). Fixed, or named by
// one neuron: Select is asked on subscribe and again whenever Registry publishes TChange, so the
// set follows that neuron's state. Select is an ordinary neuron call and may read the registry.
public abstract class Sources
{
    private protected Sources() { }

    // A fixed set of neurons (references from Get), for On<T>/React<T> of their [Signal] records.
    public static Sources Of(params object[] neurons)
    {
        ArgumentNullException.ThrowIfNull(neurons);
        return new Fixed(neurons);
    }

    // The registry is a neuron publishing TChange, a [Signal] record (such as an IText and its TextChanged); its members are
    // neuron references from Get.
    public static Sources Named<TRegistry, TChange, TMember>(TRegistry registry, Func<TRegistry, CancellationToken, Task<TMember[]>> select)
        where TRegistry : class where TChange : DigitalBrain.Signal where TMember : class
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(select);
        return new Registry<TRegistry, TChange>(registry, async (named, ct) => [.. await select(named, ct).ConfigureAwait(false)]);
    }

    // The entry points behind ISignalWatch.On<T>(Sources) and React<T>: one body in the vocabulary,
    // reached from the interface defaults and from a client connection alike.
    public static IAsyncEnumerable<T> Stream<T>(ISignalWatch brain, Sources sources, CancellationToken cancellationToken) where T : DigitalBrain.Signal
        => SourceSet.Open<T>(brain, sources, null, coalesce: false, cancellationToken);

    // The same stream, observed: subscribed runs once every first member's subscription is registered (before the
    // first read can miss a signal), reselected after each later re-selection once joiners are subscribed and leavers
    // released. A caller that must not race the set's membership waits on these instead of a pause.
    public static IAsyncEnumerable<T> Stream<T>(ISignalWatch brain, Sources sources, Func<Task>? subscribed, Action? reselected,
        CancellationToken cancellationToken) where T : DigitalBrain.Signal
        => SourceSet.Open<T>(brain, sources, subscribed, coalesce: false, cancellationToken, reselected);

    public static Task React<T>(ISignalWatch brain, Sources sources, Func<CancellationToken, Task> rebuild, CancellationToken cancellationToken) where T : DigitalBrain.Signal
        => Reaction.Run<T>(brain, sources, rebuild, cancellationToken);

    internal abstract Task<object[]> SelectAsync(CancellationToken cancellationToken);

    // Every element means "select again". onSubscribed runs once the change subscription is registered,
    // so a select made there cannot miss a change. Empty for a fixed set; the merge never opens it then.
    internal abstract IAsyncEnumerable<object> Changes(ISignalWatch brain, Func<Task> onSubscribed, CancellationToken cancellationToken);

    internal bool IsFixed => this is Fixed;

    private sealed class Fixed(object[] neurons) : Sources
    {
        internal override Task<object[]> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<object[]>([.. neurons]);
        internal override IAsyncEnumerable<object> Changes(ISignalWatch brain, Func<Task> onSubscribed, CancellationToken cancellationToken) => Empty();
        private static async IAsyncEnumerable<object> Empty() { await Task.CompletedTask; yield break; }
    }

    private sealed class Registry<TRegistry, TChange>(TRegistry registry, Func<TRegistry, CancellationToken, Task<object[]>> select) : Sources
        where TRegistry : class where TChange : DigitalBrain.Signal
    {
        internal override Task<object[]> SelectAsync(CancellationToken cancellationToken) => select(registry, cancellationToken);

        internal override async IAsyncEnumerable<object> Changes(ISignalWatch brain, Func<Task> onSubscribed,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var change in brain.On<TChange>(registry, onSubscribed, cancellationToken).ConfigureAwait(false)) { yield return change; }
        }
    }
}
