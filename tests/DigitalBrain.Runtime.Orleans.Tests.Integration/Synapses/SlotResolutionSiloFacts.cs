using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

[Capability("sql-read", 1)]
public interface ISqlRead
{
    Task<T> Scalar<T>(string sql, object? args, CancellationToken ct);
}

public sealed class FakeSqlRead(int value) : ISqlRead
{
    public Task<T> Scalar<T>(string sql, object? args, CancellationToken ct) => Task.FromResult((T)(object)value);
}

[Signal("test.sl.poke")]
public sealed record SlPoke : Signal;

[State("sl.stats")]
public sealed record SlStats(int Value)
{
    public SlStats() : this(0) { }
}

[State("sl.audit")]
public sealed record SlAudit(int Value)
{
    public SlAudit() : this(0) { }
}

public static class SlApps
{
    [Synapse(typeof(SlPoke), "sl.stats")]
    public static async Task<SlStats> Refresh(SlPoke s, SlStats state, [Slot(Name = "Db")] ISqlRead db, CancellationToken ct) =>
        state with { Value = await db.Scalar<int>("select 1", null, ct) };

    [Synapse(typeof(SlPoke), "sl.audit")]
    public static async Task<SlAudit> Refresh(SlPoke s, SlAudit state, [Slot(Name = "Db")] ISqlRead db, CancellationToken ct) =>
        state with { Value = await db.Scalar<int>("select 1", null, ct) };
}

public interface ISlPoker
{
    Task Poke();
}

[Neuron("sl.poker")]
public sealed class SlPoker : Neuron<SbPayState>, ISlPoker
{
    public Task Poke() => Save(State with { Count = State.Count + 1 }, new SlPoke());
}

// Every brain has two connected sql-read connections, analytics (7) and finance (11), and a disconnected one, broken.
internal sealed class SqlConnections : ICapabilityConnections
{
    private static readonly CapabilityId SqlRead = new("sql-read", 1);

    public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct) => Task.FromResult<IReadOnlyList<BrainConnection>>(
    [
        new("analytics", "Analytics", true, [SqlRead], null),
        new("finance", "Finance", true, [SqlRead], null),
        new("broken", "Broken", false, [SqlRead], null),
    ]);

    public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
        => Task.FromResult((TCapability)(object)new FakeSqlRead(key == "finance" ? 11 : 7));

    public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>A stateful silo whose brains list <see cref="SqlConnections"/>.</summary>
public sealed class SlotSilo : IAsyncLifetime
{
    private readonly TempHome _home = new();
    private IInProcessHost? _host;

    public IServiceProvider Services => (_host ?? throw new InvalidOperationException("Not started.")).Services;

    public T Get<T>(string id) where T : class => NeuronHosting.Get<T>(Services, NeuronId.Parse(id));

    public async ValueTask InitializeAsync()
        => _host = await new LocalSiloHostFactory(new Stores(), services => services.AddSingleton<ICapabilityConnections>(new SqlConnections()))
            .Start(new InProcessHostOptions { Home = _home.Path, Modules = [typeof(HostingModule)] }, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) { await _host.DisposeAsync(); }
        _home.Dispose();
    }

    private sealed class Stores : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo)
        {
            silo.AddBrainStorage(typeof(HostingModule).Assembly);
            silo.Services.AddSingleton<DigitalBrain.Runtime.Orleans.Operations.IConfirmationContexts>(sp => new ApprovingConfirmations(sp));
        }
    }
}

// The SlotResolutionFacts of TestOS on the silo: a binding is a connection key the brain lists as connected. Each fact has its own brain.
public sealed class SlotResolutionSiloFacts(SlotSilo silo) : IClassFixture<SlotSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IInstalls Installs(string brain) => silo.Get<IInstalls>($"{brain}/synapse.installs/default");

    private Task<InstallOutcome> Ask(string brain, string alias, string binding)
        => Installs(brain).Install(new InstallRequest(alias, new Dictionary<string, string> { ["Db"] = binding }), Ct);

    private async Task Install(string brain, string alias, string binding)
    {
        var outcome = await Ask(brain, alias, binding);
        Assert.True(outcome.Status is InstallStatus.Pending, outcome.Reason);
        await StateNeuronSiloFacts.Until(async () => await Installs(brain).Describe(alias, Ct) is not null, Ct);
        Assert.NotNull(await Installs(brain).Describe(alias, Ct));
    }

    private async Task<int> Value(string id)
    {
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, id));
        var state = await host.InspectState(Ct);
        return state is { } json && json.TryGetProperty("value", out var value) ? value.GetInt32() : 0;
    }

    [Fact]
    public async Task AReactionUsesTheBoundConnection()
    {
        await Install("sla", "sl.stats", "analytics");

        await silo.Get<ISlPoker>("sla/sl.poker/one").Poke();
        await StateNeuronSiloFacts.Until(async () => await Value("sla/sl.stats/default") == 7, Ct);

        Assert.Equal(7, await Value("sla/sl.stats/default"));
    }

    [Fact]
    public async Task AnUnboundSlotIsRefusedAtInstall()
    {
        var missing = await Ask("slb", "sl.stats", "missing");
        var broken = await Ask("slb", "sl.stats", "broken");

        Assert.Equal(InstallStatus.Refused, missing.Status);
        Assert.Contains("missing", missing.Reason, StringComparison.Ordinal);
        Assert.Equal(InstallStatus.Refused, broken.Status);
        Assert.Contains("not connected", broken.Reason, StringComparison.Ordinal);
        Assert.Empty(await Installs("slb").Pending(Ct));
    }

    [Fact]
    public async Task TwoInstallsResolveTheirOwnBindings()
    {
        await Install("slc", "sl.stats", "analytics");
        await Install("slc", "sl.audit", "finance");

        await silo.Get<ISlPoker>("slc/sl.poker/one").Poke();
        await StateNeuronSiloFacts.Until(async () => await Value("slc/sl.stats/default") == 7 && await Value("slc/sl.audit/default") == 11, Ct);

        Assert.Equal(7, await Value("slc/sl.stats/default"));
        Assert.Equal(11, await Value("slc/sl.audit/default"));
    }
}
