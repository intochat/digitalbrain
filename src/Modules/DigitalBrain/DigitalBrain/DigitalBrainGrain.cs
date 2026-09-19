using DigitalBrain.Contracts;
using Orleans.BroadcastChannel;

namespace DigitalBrain.Core;

internal interface IDigitalBrainLive : IGrainWithStringKey
{
    Task<IReadOnlyList<Signal>> Signals();
}

[GrainType("digitalbrain")]
[ImplicitChannelSubscription(NeuronBroadcast.Provider)]
public sealed class DigitalBrainGrain : Grain, IDigitalBrainLive, IOnBroadcastChannelSubscribed
{
    private const int Keep = 1024;
    private readonly List<Signal> _signals = [];

    public Task<IReadOnlyList<Signal>> Signals() => Task.FromResult<IReadOnlyList<Signal>>(_signals.ToArray());

    public Task OnSubscribed(IBroadcastChannelSubscription subscription)
        => subscription.Attach<Signal>(Heard, _ => Task.CompletedTask);

    private Task Heard(Signal signal)
    {
        if (_signals.Count == Keep)
        {
            _signals.RemoveAt(0);
        }

        _signals.Add(signal);
        return Task.CompletedTask;
    }
}
