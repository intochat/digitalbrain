using DigitalBrain.Units;
using System.Reflection;
using DigitalBrain.Rules;
using DigitalBrain.Kernel;
using DigitalBrain.Testing.Tests.Unit.Synapses;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Installs;

// Reaction edges come from the install, brain-wide; gates are found only for installed aliases.
public sealed class InstallEdgesFacts
{
    private static readonly NeuronId Installs = NeuronId.Parse("ie/synapse.installs/default");
    private static readonly NeuronId Books = NeuronId.Parse("ie/ledger/default");
    private static readonly NeuronId One = NeuronId.Parse("ie/payments/one");
    private static readonly NeuronId Two = NeuronId.Parse("ie/payments/two");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OS Start()
    {
        var os = OS.Local().Register(typeof(StateNeuronOnTestOSFacts).Assembly);
        os.Confirmations.ApproveAll();
        return os;
    }

    private static async Task Install(OS os, string alias)
    {
        var outcome = await os.Get<IInstalls>(Installs).Install(new InstallRequest(alias, new Dictionary<string, string>()), Ct);
        Assert.Equal(InstallStatus.Pending, outcome.Status);
        await os.Idle();
        Assert.NotNull(await os.Get<IInstalls>(Installs).Describe(alias, Ct));
    }

    private static async Task<decimal?> Spent(OS os)
    {
        var state = await ((IBrainObserver)os).State(Books, Ct);
        return state?.GetProperty("spent").GetDecimal();
    }

    [Fact]
    public async Task ARecordedInstallWiresReactionsFromEveryPublisher()
    {
        await using var os = Start();
        await Install(os, "ledger");

        await os.Get<IPayments>(One).Charge(5m);
        await os.Get<IPayments>(Two).Charge(7m);
        await os.Idle();

        Assert.Equal(12m, await Spent(os));
        var synapse = Assert.Single(os.SynapsesOf(Books));
        Assert.Equal(new Synapse(new NeuronId(Books.Brain, "*", "*"), typeof(Charging), Books), synapse);
        Assert.Contains(new ObservedSynapse("*", Books.ToString(), "test.synapse.charging"), await ((IBrainObserver)os).Synapses(Ct));
        Assert.Empty(os.Failures);
    }

    // A plan-1 per-source edge on the state neuron, as the registry holds it; the TestOS keeps it internal, so it is reached by reflection.
    private static async Task EnsurePerSource(OS os, NeuronId subscriber, NeuronId from, Type signal)
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var registry = typeof(OS).GetProperty("Delivery", Any)!.GetValue(os)!;
        var host = typeof(OS).GetMethod("Host", Any, [typeof(NeuronId)])!.Invoke(os, [subscriber])!;
        var ensure = registry.GetType().GetMethods(Any).Single(m => m.Name == "Ensure" && m.GetParameters().Length == 3);
        await (Task)ensure.Invoke(registry, [host, from, new[] { signal }])!;
    }

    [Fact]
    public async Task AnInstallReplacesPerSourceEdgesWithTheBrainWideOne()
    {
        await using var os = Start();
        await EnsurePerSource(os, Books, One, typeof(Charging));
        Assert.Single(os.SynapsesOf(Books));

        await Install(os, "ledger");
        await os.Get<IPayments>(One).Charge(5m);
        await os.Idle();

        Assert.Equal(5m, await Spent(os));
        Assert.Single(os.Journal(Books.Brain).Signals(Books), s => s.Signal is StateChanged);
        var synapse = Assert.Single(os.SynapsesOf(Books));
        Assert.Equal(new NeuronId(Books.Brain, "*", "*"), synapse.From);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task AnUninstalledStateReceivesNothing()
    {
        await using var os = Start();

        await os.Get<IPayments>(One).Charge(5m);
        await os.Activate(Books);
        await os.Idle();

        Assert.Equal(0m, await Spent(os) ?? 0m);
        Assert.Empty(os.SynapsesOf(Books));
        Assert.Empty(os.Journal(Books.Brain).Signals(Books));
    }

    [Fact]
    public async Task RetireStopsDelivery()
    {
        await using var os = Start();
        await Install(os, "ledger");
        await os.Get<IPayments>(One).Charge(5m);
        await os.Idle();

        await os.Get<IInstalls>(Installs).Retire("ledger", Ct);
        await os.Idle();
        await os.Get<IPayments>(Two).Charge(7m);
        await os.Idle();

        Assert.Equal(5m, await Spent(os));
        Assert.Empty(os.SynapsesOf(Books));
    }

    [Fact]
    public async Task AReactionInstalledLaterIsFoundOnTheNextPublish()
    {
        await using var os = Start();
        var pay = os.Get<IPayments>(One);
        await pay.Charge(400m);
        await os.Idle();

        await Install(os, "ledger");
        await pay.Charge(400m);
        await os.Idle();

        Assert.Equal(400m, await Spent(os));
    }
}
