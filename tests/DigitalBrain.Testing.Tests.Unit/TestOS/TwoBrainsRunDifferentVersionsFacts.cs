using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using DigitalBrain.Testing.Tests.Unit.Host;
using OS = DigitalBrain.Testing.TestOS;
using Xunit;

namespace DigitalBrain.Testing.Tests.Unit.Host;

public sealed class TwoBrainsRunDifferentVersionsFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static IHsPayments Pay(OS os, string brain) => os.Get<IHsPayments>(NeuronId.Parse($"{brain}/hs.payments/p"));

    private static async Task<decimal> Spent(OS os, string brain)
    {
        var state = await ((IBrainObserver)os).State(NeuronId.Parse($"{brain}/hs.week/default"), Ct);
        return state!.Value.GetProperty("spent").GetDecimal();
    }

    [Fact]
    public async Task TwoBrainsRunDifferentVersionsIsolated()
    {
        await using var os = OS.Local().Register<HsPayments>();
        var b1 = Brain("brain-a");
        var b2 = Brain("brain-b");

        // Brain A deploys Version 1 (x1 multiplier)
        var outcomeA = await os.Synapses.Deploy(b1, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeA.Status);
        await os.Confirmations.Approve(outcomeA.Installs["hs.week"].Reason!);

        // Brain B deploys Version 2 (x2 multiplier)
        var outcomeB = await os.Synapses.Deploy(b2, HsSource.Of(HsSource.Cap200), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeB.Status);
        await os.Confirmations.Approve(outcomeB.Installs["hs.week"].Reason!);
        await os.Idle();

        // Charge 50 in Brain A -> 50 spent
        await Pay(os, "brain-a").Charge(50m);
        await os.Idle();
        Assert.Equal(50m, await Spent(os, "brain-a"));

        // Charge 50 in Brain B -> 100 spent (x2 multiplier)
        await Pay(os, "brain-b").Charge(50m);
        await os.Idle();
        Assert.Equal(100m, await Spent(os, "brain-b"));

        // Brain A retires; Brain B remains completely unaffected
        var retireA = await os.Synapses.Retire(b1, HsSource.Key, Ct);
        Assert.Equal(DeployStatus.Retired, retireA.Status);
        await os.Idle();

        // Charge in Brain B still works
        await Pay(os, "brain-b").Charge(25m);
        await os.Idle();
        Assert.Equal(150m, await Spent(os, "brain-b"));
    }
}
