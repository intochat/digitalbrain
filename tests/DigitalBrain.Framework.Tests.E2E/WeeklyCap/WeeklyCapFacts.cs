using DigitalBrain.Units;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Framework.Tests.E2E.WeeklyCap;

public sealed class WeeklyCapFacts
{
    private static readonly NeuronId Pay = NeuronId.Parse("b/payments/p");
    private static readonly NeuronId Week = NeuronId.Parse("b/week/default");

    private static async Task<OS> Start()
    {
        var os = OS.Local().Register(typeof(WeeklyCapFacts).Assembly);
        os.Confirmations.ApproveAll();
        var installs = os.Get<IInstalls>(NeuronId.Parse("b/synapse.installs/default"));
        await installs.Install(new("week", new Dictionary<string, string>()), TestContext.Current.CancellationToken);
        await os.Idle();
        return os;
    }

    private static async Task<decimal> Spent(OS os)
    {
        var state = await ((IBrainObserver)os).State(Week, TestContext.Current.CancellationToken);
        return state!.Value.GetProperty("spent").GetDecimal();
    }

    [Fact]
    public async Task ThePromiseHoldsAcrossAWeekOfCharges()
    {
        await using var os = await Start();
        var pay = os.Get<IPayments>(Pay);

        await pay.Charge(120m);
        await pay.Charge(100m);
        await pay.Charge(70m);
        await os.Idle();

        Assert.Equal(290m, await Spent(os));

        await pay.Roll(new DateOnly(2026, 11, 9));
        await os.Idle();
        Assert.Equal(0m, await Spent(os));

        await pay.Charge(20m);
        await os.Idle();
        Assert.Equal(20m, await Spent(os));
    }
}
