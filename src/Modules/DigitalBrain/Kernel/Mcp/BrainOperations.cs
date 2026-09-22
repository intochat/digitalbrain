using System.Text.Json;
using DigitalBrain.Contracts;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Mcp;

// The client surface over the typed neuron model. The generic invoker,
// descriptors and string signal vocabulary were deleted with the old
// abstractions, so only operations the model actually supports remain:
// resolve a handle, probe it with Watch/Unwatch, and observe its signals.
public sealed class BrainOperations(IDigitalBrain brain, IGrainFactory grains)
{
    public const int MaxObserveSeconds = 60;

    public GetResult Get(string session, string neuron)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neuron);
        var handle = brain.Get<INeuron>(neuron);
        return new(session, neuron, handle.GetGrainId().ToString());
    }

    // A watch is the only generic probe the contract offers: attaching and
    // detaching proves reachability and returns the activation id, which
    // changes whenever the neuron reactivates.
    public async Task<PingResult> PingAsync(string neuron, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neuron);
        var handle = brain.Get<INeuron>(neuron);
        var observer = grains.CreateObjectReference<INeuronObserver>(SignalSink.Instance);
        try
        {
            var activation = await handle.Watch(observer).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(handle.GetGrainId().ToString(), activation.ToString());
        }
        finally
        {
            await handle.Unwatch(observer).ConfigureAwait(false);
        }
    }

    // Signals carry data, so there is no journal to replay; observing is a live
    // window. Subscribing as the base Signal type receives every published signal.
    public async Task<ObserveResult> ObserveAsync(string neuron, int seconds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neuron);
        if (seconds is < 0 or > MaxObserveSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, $"Observe between 0 and {MaxObserveSeconds} seconds.");
        }

        var handle = brain.Get<INeuron>(neuron);
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(TimeSpan.FromSeconds(seconds));
        List<SignalEntry> signals = [];
        try
        {
            await using var subscription = await brain.SubscribeAsync<Signal>(handle, window.Token).ConfigureAwait(false);
            await foreach (var signal in subscription.ReadAllAsync(window.Token).ConfigureAwait(false))
            {
                signals.Add(Describe(signal));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return new(handle.GetGrainId().ToString(), seconds, signals);
    }

    private static SignalEntry Describe(Signal signal)
    {
        var type = signal.GetType();
        return new(type.FullName ?? type.Name, JsonSerializer.Serialize(signal, type));
    }
}

// An observer that drops signals; ping only needs the watch round trip.
internal sealed class SignalSink : INeuronObserver
{
    public static readonly SignalSink Instance = new();

    private SignalSink() { }

    public Task OnSignalAsync(Signal signal) => Task.CompletedTask;
}