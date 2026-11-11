using DigitalBrain.Spi;

namespace DigitalBrain.Kernel;

internal interface IAdmission
{
    Task Admit(NeuronId subscriber, NeuronId publisher, string[] signalAliases, CancellationToken ct = default);
}

internal sealed class Subscribing(
    NeuronId subscriber,
    bool stateless,
    Delivery delivery,
    IAdmission admission,
    TimeProvider time) : ISynapses
{
    public Task Ensure<TSignal>(NeuronId from) where TSignal : Signal => Ensure(from, [typeof(TSignal)]);

    public async Task Ensure(NeuronId from, params Type[] signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (stateless) { throw new InvalidOperationException($"{subscriber.Type} is a [StatelessNeuron] class: it cannot hold synapses."); }
        if (signals.Length == 0) { throw new ArgumentException("At least one signal type is required.", nameof(signals)); }
        ValidateSignals(signals);
        var aliases = signals.Select(SignalAliases.Of).ToArray();
        await admission.Admit(subscriber, from, aliases, CancellationToken.None).ConfigureAwait(false);
        await delivery.Ensure(from, aliases, time.GetUtcNow()).ConfigureAwait(false);
    }

    public Task Remove(NeuronId from) => delivery.Remove(from);

    public Task EnsureBrainWide(params Type[] signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (stateless) { throw new InvalidOperationException($"{subscriber.Type} is a [StatelessNeuron] class: it cannot subscribe to brain-wide signals."); }
        if (signals.Length == 0) { throw new ArgumentException("At least one signal type is required.", nameof(signals)); }
        ValidateSignals(signals);
        return delivery.EnsureBrainWide(signals.Select(SignalAliases.Of).ToArray(), time.GetUtcNow(), exact: false);
    }

    public Task EnsureBrainWide(IReadOnlyList<string> signalAliases)
    {
        ArgumentNullException.ThrowIfNull(signalAliases);
        if (stateless) { throw new InvalidOperationException($"{subscriber.Type} is a [StatelessNeuron] class: it cannot subscribe to brain-wide signals."); }
        if (signalAliases.Count == 0) { throw new ArgumentException("At least one signal alias is required.", nameof(signalAliases)); }
        return delivery.EnsureBrainWide(signalAliases, time.GetUtcNow(), exact: false);
    }

    public Task RemoveBrainWide() => delivery.RemoveBrainWide();

    private static void ValidateSignals(IEnumerable<Type> signals)
    {
        foreach (var type in signals)
        {
            if (type is null) { throw new ArgumentException("A signal type is null.", nameof(signals)); }
            if (!typeof(Signal).IsAssignableFrom(type)) { throw new ArgumentException($"Type '{type.Name}' does not inherit from Signal.", nameof(signals)); }
        }
    }
}

internal static class Reactions
{
    public static async Task Dispatch(Neuron neuron, JournaledSignal signal, CancellationToken ct)
    {
        if (neuron is ISynapseReceiver receiver)
        {
            await receiver.Receive(signal, ct).ConfigureAwait(false);
        }
        else if (neuron is ISignalSink sink)
        {
            await sink.Deliver(signal).ConfigureAwait(false);
        }
        else
        {
            await ReactionTable.React(neuron, signal.Signal, ct).ConfigureAwait(false);
        }
    }
}
