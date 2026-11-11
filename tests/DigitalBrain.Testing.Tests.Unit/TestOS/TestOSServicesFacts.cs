using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

public sealed record DeployerState
{
    public int Deploys { get; init; }
}

public interface IDeployer
{
    Task<string> Deploy(string text);
}

// A product neuron that compiles and deploys a unit through the services the host serves it, as an os studio does on the silo.
[Neuron("svc.deployer")]
public sealed class Deployer : Neuron<DeployerState>, IDeployer
{
    public async Task<string> Deploy(string text)
    {
        var compiler = Context.Services.GetService(typeof(SynapseCompiler)) as SynapseCompiler ?? throw new InvalidOperationException("no compiler");
        var host = Context.Services.GetService(typeof(ISynapseHost)) as ISynapseHost ?? throw new InvalidOperationException("no synapse host");
        var source = new SynapseSource(HsSource.Key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);
        if (!compiler.Compile(source).Success) { return "compile failed"; }
        var outcome = await host.Deploy(Id.Brain, source, new Dictionary<string, IReadOnlyDictionary<string, string>>(), CancellationToken.None);
        return outcome.Status.ToString();
    }
}

public sealed class TestOSServicesFacts
{
    private static readonly string ReadsDb = HsSource.WithDb
        .Replace("public static WeekSpend WeeklyCap", "public static async Task<WeekSpend> WeeklyCap", StringComparison.Ordinal)
        .Replace("Spent = t.Spent + s.Amount", "Spent = t.Spent + await Db.Scalar<int>(\"select\", null, CancellationToken.None)", StringComparison.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANeuronResolvesTheCompilerAndTheHostAndDeploysThroughThem()
    {
        await using var os = OS.Local().Register<Deployer>().Register<HsPayments>();

        Assert.Same(os.Synapses, os.Services.GetService(typeof(ISynapseHost)));
        var status = await os.Get<IDeployer>(NeuronId.Parse("sv1/svc.deployer/a")).Deploy(HsSource.Budget);
        await os.Idle();

        Assert.Equal(nameof(DeployStatus.Deployed), status);
        Assert.Equal(DeployStatus.Deployed, Assert.Single(await os.Synapses.List(NeuronId.Parse("sv1/x/y").Brain, Ct)).Status);
    }

    [Fact]
    public async Task ASourceBindingNamesAConnectionOfTheHostsCapabilityConnections()
    {
        var connections = new Connections();
        await using var os = OS.Local(o => o.Services = new Provider(connections)).Register<HsPayments>();
        var brain = NeuronId.Parse("sv2/x/y").Brain;
        var source = HsSource.Of(ReadsDb) with { Bindings = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["hs.week"] = new Dictionary<string, string> { ["Db"] = "conn-x" } } };

        var outcome = await os.Synapses.Deploy(brain, source, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.True(outcome.Status == DeployStatus.Deployed, outcome.Reason);
        Assert.Equal(InstallStatus.Pending, outcome.Installs["hs.week"].Status);
        await os.Confirmations.Approve(outcome.Installs["hs.week"].Reason!);
        await os.Idle();

        await os.Get<IHsPayments>(NeuronId.Parse("sv2/hs.payments/p")).Charge(1m);
        await os.Idle();
        var state = await ((IBrainObserver)os).State(NeuronId.Parse("sv2/hs.week/default"), Ct);
        Assert.Equal(5m, state!.Value.GetProperty("spent").GetDecimal());
        Assert.Equal(["conn-x:hs.week"], connections.Bound);
    }

    private sealed class Provider(ICapabilityConnections connections) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ICapabilityConnections) ? connections : null;
    }

    private sealed class Connections : ICapabilityConnections
    {
        public List<string> Bound { get; } = [];

        public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BrainConnection>>([new BrainConnection("conn-x", "X", true, [new CapabilityId("sql-read", 1)], null)]);

        public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
        {
            Bound.Add($"{key}:{callerApp}");
            return Task.FromResult((TCapability)(object)new FakeSqlRead(5));
        }

        public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct) => Task.CompletedTask;
    }
}
