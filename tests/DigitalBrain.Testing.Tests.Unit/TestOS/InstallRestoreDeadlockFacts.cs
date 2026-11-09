using DigitalBrain.Spi;
using DigitalBrain.Time;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

// Approving an ask whose alias is missing (after a restart) restores the source, which takes the synapse host's gate. A deploy that holds
// the gate and calls the installs neuron must not wait behind that approval, nor the approval behind the deploy.
public sealed class InstallRestoreDeadlockFacts
{
    // Two state aliases, each with a slot: the deploy calls Install twice while it holds the gate.
    private const string Second = """
        using DigitalBrain.Testing.Tests.Unit.Slots;

        namespace Me.Second;

        [Signal("sc.one-hit")]
        public sealed record OneHit(decimal Amount) : Signal;

        [Signal("sc.two-hit")]
        public sealed record TwoHit(decimal Amount) : Signal;

        [State("sc.one")]
        public sealed record One(decimal Spent = 0)
        {
            public One() : this(0) { }
        }

        [State("sc.two")]
        public sealed record Two(decimal Spent = 0)
        {
            public Two() : this(0) { }
        }

        public static class Units
        {
            [Synapse(typeof(OneHit), "sc.one")]
            public static Check First(OneHit s, One t, [Slot] ISqlRead Db) => Check.Allow(t with { Spent = t.Spent + s.Amount });

            [Synapse(typeof(TwoHit), "sc.two")]
            public static Check Second(TwoHit s, Two t, [Slot] ISqlRead Db) => Check.Allow(t with { Spent = t.Spent + s.Amount });
        }
        """;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    [Fact]
    public async Task ApprovingAMissingAliasWhileADeployHoldsTheGateDoesNotDeadlock()
    {
        var hooks = new BlockingHooks();
        await using var os = OS.Local(o => o.Services = new Services(hooks)).Register<HsPayments>().With<ISqlRead>("analytics", new FakeSqlRead(1));
        var brain = Brain("dl1");
        var asked = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(InstallStatus.Pending, asked.Installs["hs.week"].Status);
        await os.Idle();
        await os.Restart();
        Assert.DoesNotContain("hs.week", os.Catalog.States.Keys);

        // The deploy takes the gate and blocks inside its first Install (the slot's binding check).
        var bindings = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["sc.one"] = new Dictionary<string, string> { ["Db"] = "analytics" },
            ["sc.two"] = new Dictionary<string, string> { ["Db"] = "analytics" },
        };
        var deploy = os.Synapses.Deploy(brain, new SynapseSource("Me.Second", new Dictionary<string, string> { ["Units.cs"] = Second }, null), bindings, Ct);
        await hooks.Entered.Task.WaitAsync(Bound, Ct);

        // The answer to the old ask queues behind that Install; once it runs, the deploy still holds the gate and calls Install again.
        var approve = os.Confirmations.Approve(asked.Installs["hs.week"].Reason!);
        await Task.Delay(200, Ct);
        hooks.Release.SetResult();

        var deployed = await deploy.WaitAsync(Bound, Ct);
        await approve.WaitAsync(Bound, Ct);
        await os.Idle().WaitAsync(Bound, Ct);
        var installs = NeuronId.Parse("dl1/synapse.installs/default");
        var until = DateTimeOffset.UtcNow + Bound;
        while (DateTimeOffset.UtcNow < until && !os.Journal(brain).Signals(installs).Any(s => s.Signal is Installed { Alias: "hs.week" })) { await Task.Delay(100, Ct); }

        Assert.Equal(DeployStatus.Deployed, deployed.Status);
        var journal = os.Journal(brain).Signals(installs);
        Assert.Contains(journal, s => s.Signal is Installed { Alias: "hs.week" });
        Assert.DoesNotContain(journal, s => s.Signal is InstallDenied { Alias: "hs.week" });
        Assert.Contains("hs.week", os.Catalog.States.Keys);
    }

    private sealed class Services(IInstallHooks hooks) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IInstallHooks) ? hooks : null;
    }

    // Blocks the first binding check until released; everything else is a no-op.
    private sealed class BlockingHooks : IInstallHooks
    {
        private int calls;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string?> ValidateBinding(NeuronId state, SlotDescriptor slot, string binding, CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Entered.SetResult();
                await Release.Task.ConfigureAwait(false);
            }

            return null;
        }

        public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, CancellationToken ct) => Task.CompletedTask;

        public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, IReadOnlyList<string> signalAliases, CancellationToken ct) => Task.CompletedTask;

        public Task RemoveReactionEdges(NeuronId state, CancellationToken ct) => Task.CompletedTask;

        public Task RegisterSchedules(NeuronId state, IReadOnlyList<Schedule> schedules, CancellationToken ct) => Task.CompletedTask;

        public Task UnregisterSchedules(NeuronId state, CancellationToken ct) => Task.CompletedTask;
    }
}
