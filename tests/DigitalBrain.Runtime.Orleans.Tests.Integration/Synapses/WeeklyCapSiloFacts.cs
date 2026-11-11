using DigitalBrain.Framework.Tests.E2E.WeeklyCap;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public abstract class WeeklyCapSiloBase(StatefulSilo silo)
{
    protected static readonly NeuronId Pay = NeuronId.Parse("wc/payments/p");
    protected static readonly NeuronId Week = NeuronId.Parse("wc/week/default");

    protected StatefulSilo Silo { get; } = silo;

    protected IPayments Payments => Silo.Get<IPayments>(Pay.ToString());

    protected async Task<decimal?> Spent()
    {
        var host = Silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, Week.ToString()));
        var state = await host.InspectState(TestContext.Current.CancellationToken);
        return state is { } json && json.TryGetProperty("spent", out var prop) ? prop.GetDecimal() : null;
    }

    protected Task Install() => StateNeuronSiloFacts.Install(Silo.Services, "wc", "week");

    protected Task SpentReaches(decimal expected)
        => StateNeuronSiloFacts.Until(async () => await Spent() == expected, TestContext.Current.CancellationToken);
}

public sealed class WeeklyCapSiloFacts(StatefulSilo silo) : WeeklyCapSiloBase(silo), IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task ThePromiseHoldsAcrossAWeekOfCharges()
    {
        await Install();
        await Payments.Charge(120m);
        await Payments.Charge(100m);
        await Payments.Charge(70m);
        await SpentReaches(290m);
        Assert.Equal(290m, await Spent());

        await Payments.Roll(new DateOnly(2026, 11, 9));
        await SpentReaches(0m);
        Assert.Equal(0m, await Spent());

        await Payments.Charge(20m);
        await SpentReaches(20m);
        Assert.Equal(20m, await Spent());
    }
}
