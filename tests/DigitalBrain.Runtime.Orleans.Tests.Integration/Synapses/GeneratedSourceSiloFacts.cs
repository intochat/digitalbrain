using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// The weekly cap as text (ShSource, with the brain's own aliases) from deploy to refusal and from retire to nothing left, on the silo.
public sealed class GeneratedSourceSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SynapseHost Host => (SynapseHost)silo.Services.GetRequiredService<ISynapseHost>();

    private IShPayments Pay(string brain) => silo.Get<IShPayments>($"{brain}/sh.payments/p");

    private IInstalls Installs(string brain) => silo.Get<IInstalls>($"{brain}/synapse.installs/default");

    private async Task<DeployOutcome> Deploy(string brain)
    {
        var outcome = await Host.Deploy(ShSource.Brain(brain), ShSource.Of(brain, ShSource.Budget), ShSource.None, Ct);
        Assert.True(outcome.Status == DeployStatus.Deployed, $"{outcome.Status}: {outcome.Reason} {string.Join("; ", outcome.Diagnostics)}");
        await StateNeuronSiloFacts.Until(async () => await Installs(brain).Describe(ShSource.Week(brain), Ct) is not null, Ct);
        return outcome;
    }

    [Fact]
    public async Task ThePromiseHoldsFromTextToRefusal()
    {
        await Deploy("ge1");
        var pay = Pay("ge1");

        await pay.Charge(120m);
        await pay.Charge(100m);
        await pay.Charge(70m);
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => pay.Charge(20m));

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
    }

    [Fact]
    public async Task RetireFreesEverything()
    {
        var first = await Deploy("ge2");
        await Assert.ThrowsAsync<RuleViolationException>(() => Pay("ge2").Charge(350m));

        var retired = await Host.Retire(ShSource.Brain("ge2"), ShSource.Key("ge2"), Ct);

        Assert.Equal((DeployStatus.Retired, first.Hash), (retired.Status, retired.Hash));
        Assert.Empty(await Host.List(ShSource.Brain("ge2"), Ct));
        Assert.DoesNotContain(ShSource.Week("ge2"), silo.Services.GetRequiredService<ISynapseCatalogSource>().Current.States.Keys);
        // No gate is left on the alias: the publisher finds no Charging signal to publish, so nothing refuses a charge.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Pay("ge2").Charge(350m));
        Assert.Empty(Host.Leaked);
    }
}
