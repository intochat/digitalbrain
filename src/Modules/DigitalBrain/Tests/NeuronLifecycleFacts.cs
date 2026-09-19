using DigitalBrain.Contracts;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class NeuronLifecycleFacts
{
    [Fact]
    public async Task BroadcastsActivationAndDeactivationToTheBrain()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync();
        Assert.Empty(await brain.Signals());

        var neuron = brain.Get<INeuron>("alice");
        await neuron.Ping();

        var activated = await WaitFor<NeuronActivated>(brain, cancellation);
        Assert.Equal("alice", activated.NeuronId);

        await neuron.Sleep();
        var deactivated = await WaitFor<NeuronDeactivated>(brain, cancellation);
        Assert.Equal("alice", deactivated.NeuronId);
    }

    private static async Task<T> WaitFor<T>(IDigitalBrain brain, CancellationToken cancellation)
        where T : Signal
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if ((await brain.Signals()).OfType<T>().LastOrDefault() is { } match)
            {
                return match;
            }

            await Task.Delay(20, cancellation);
        }

        throw new TimeoutException($"Did not receive {typeof(T).Name}.");
    }
}
