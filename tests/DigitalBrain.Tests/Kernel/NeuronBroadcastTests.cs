using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

// Guards the observable broadcast contract across the transport swap (hand-rolled memory-stream subscription
// -> Orleans AddBroadcastChannel / [ImplicitChannelSubscription]): a broadcast fired by one grain must reach a
// different, already-activated grain that declares a matching IHandle<T>, and be recorded in its incoming journal.
public class NeuronBroadcastTests : NeuronTestBase
{
    [Fact]
    public async Task Broadcast_Reaches_A_Different_Grain_Via_Implicit_Channel_Subscription()
    {
        var sender = Grain<IProbeNeuron>("broadcast-sender");
        var receiver = Grain<IProbeNeuron>("broadcast-receiver");

        // Activate the receiver first so its implicit channel subscription is established before the broadcast.
        await receiver.FireAsync(new NeuronActivated(new NeuronId("broadcast-receiver")));

        await sender.FireAsync(new ProbeMessageSynapse("channel-probe") with { IsBroadcast = true });

        // Broadcast delivery is asynchronous (Orleans fans it out off the publisher's turn), so poll.
        IReadOnlyList<Synapse> incoming = Array.Empty<Synapse>();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            incoming = await receiver.GetIncomingTimelineAsync();
            if (incoming.Any(s => s is ProbeMessageSynapse d && d.Text == "channel-probe"))
                return;
            await Task.Delay(50);
        }

        Assert.Contains(incoming, s => s is ProbeMessageSynapse d && d.Text == "channel-probe");
    }
}
