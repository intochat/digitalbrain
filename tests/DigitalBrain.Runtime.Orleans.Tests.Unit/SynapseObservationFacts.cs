using DigitalBrain.Kernel;
using OrleansSdk = Orleans;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class SynapseObservationFacts
{
    [Fact]
    public void A_neuron_host_grain_id_names_the_subscriber()
    {
        var id = OrleansSdk.Runtime.GrainId.Create("neuron.host", "ada/rx.sink/k");
        Assert.Equal("ada/rx.sink/k", SynapseObservation.SubscriberOf(id.ToString()));
        Assert.Null(SynapseObservation.SubscriberOf("kernel.instances/ada"));
    }

    [Fact]
    public async Task DeliveryHub_registers_listener_synchronously_before_first_move_next()
    {
        var hub = new DeliveryHub();
        var token = TestContext.Current.CancellationToken;
        var expected = new SignalDelivery("ada/rx.source/s", "ada/rx.sink/k", "test.number", 1);

        var stream = hub.Listen("ada", token);
        hub.Publish("ada", expected);

        var seen = new List<SignalDelivery>();
        await foreach (var delivery in stream)
        {
            seen.Add(delivery);
            break;
        }

        Assert.Equal([expected], seen);
    }

    [Fact]
    public async Task DeliveryHub_canceled_before_enumeration_throws_when_iterated()
    {
        var hub = new DeliveryHub();
        using var cts = new CancellationTokenSource();

        var stream = hub.Listen("ada", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in stream)
            {
            }
        });
    }
}
