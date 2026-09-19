using DigitalBrain.Contracts;
using Orleans.BroadcastChannel;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class NeuronLifecycleFacts
{
    [Fact]
    public async Task BroadcastsActivationAndDeactivationToSubscribers()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var brain = await BrainSimulation.StartAsync();
        var sink = brain.Get<INeuronLifecycleSink>(NeuronBroadcast.Everyone);
        Assert.Empty(await sink.Heard());

        var neuron = brain.Get<INeuron>("alice");
        await neuron.Ping();

        var activated = await WaitFor<NeuronActivated>(sink, cancellation);
        Assert.Equal("alice", activated.NeuronId);

        await neuron.Sleep();
        var deactivated = await WaitFor<NeuronDeactivated>(sink, cancellation);
        Assert.Equal("alice", deactivated.NeuronId);
    }

    private static async Task<T> WaitFor<T>(INeuronLifecycleSink sink, CancellationToken cancellation)
        where T : Signal
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await sink.Heard() is { } heard && heard.OfType<T>().LastOrDefault() is { } match)
            {
                return match;
            }

            await Task.Delay(20, cancellation);
        }

        throw new TimeoutException($"Did not receive {typeof(T).Name}.");
    }
}

public interface INeuronLifecycleSink : IGrainWithStringKey
{
    Task<IReadOnlyList<Signal>> Heard();
}

[ImplicitChannelSubscription(NeuronBroadcast.Provider)]
public sealed class NeuronLifecycleSink : Grain, INeuronLifecycleSink, IOnBroadcastChannelSubscribed
{
    private readonly List<Signal> _heard = [];

    public Task<IReadOnlyList<Signal>> Heard() => Task.FromResult<IReadOnlyList<Signal>>(_heard.ToArray());

    public Task OnSubscribed(IBroadcastChannelSubscription subscription)
        => subscription.Attach<Signal>(signal =>
        {
            _heard.Add(signal);
            return Task.CompletedTask;
        }, _ => Task.CompletedTask);
}
