using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// 04 D-08, D-12, T-04-36, T-04-37: subscribe-time admission and the explicit subscription.
public sealed class SynapseAdmissionFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    [Fact]
    public async Task A_source_that_refuses_the_subscriber_makes_Ensure_throw_and_no_subscription_exists()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => silo.Get<IListener>("b1/probe.listener/denied").Follow("b1/probe.guarded/g"));
        Assert.Equal(0, await silo.Subscriptions("b1/probe.listener/denied", "probe.counted"));

        await silo.Get<IListener>("b1/probe.listener/ok-admitted").Follow("b1/probe.guarded/g");
        Assert.Equal(1, await silo.Subscriptions("b1/probe.listener/ok-admitted", "probe.counted"));
        await silo.Get<IGuarded>("b1/probe.guarded/g").Add(3);
        await Heard.Until("b1/probe.listener/ok-admitted", 1, TestContext.Current.CancellationToken);
        Assert.Equal(["counted:3"], Heard.Of("b1/probe.listener/ok-admitted"));
    }

    [Fact]
    public async Task A_self_synapse_and_a_source_of_another_brain_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => silo.Get<IListener>("b1/probe.listener/self").Follow("b1/probe.listener/self"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => silo.Get<IListener>("b1/probe.listener/foreign").Follow("b2/probe.counter/x"));
        Assert.Equal(0, await silo.Subscriptions("b1/probe.listener/self", "probe.counted"));
        Assert.Equal(0, await silo.Subscriptions("b1/probe.listener/foreign", "probe.counted"));
    }

    [Fact]
    public async Task Two_sources_on_one_namespace_share_one_subscription_and_Ensure_again_adds_none()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/shared";
        var listener = silo.Get<IListener>(Listener);
        await listener.Follow("b1/probe.counter/s1");
        await listener.Follow("b1/probe.counter/s2");
        await listener.Follow("b1/probe.counter/s1");
        Assert.Equal(1, await silo.Subscriptions(Listener, "probe.counted"));

        await silo.Get<ICounter>("b1/probe.counter/s1").Add(1);
        await silo.Get<ICounter>("b1/probe.counter/s2").Add(20);
        await Heard.Until(Listener, 2, ct);
        Assert.Equal(["counted:1", "counted:20"], Heard.Of(Listener).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Ensure_on_a_StatelessNeuron_throws_because_stateless_activations_own_no_durable_synapse()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => silo.Get<IListener>("b1/probe.stateless.listener/x").Follow("b1/probe.counter/x"));
        Assert.Contains("StatelessNeuron", error.Message);
    }
}
