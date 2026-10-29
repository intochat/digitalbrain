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
}
