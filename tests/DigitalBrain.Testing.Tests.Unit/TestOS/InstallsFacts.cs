using System.Collections.Concurrent;
using DigitalBrain.Confirmation;
using DigitalBrain.Spi;
using DigitalBrain.Testing.Tests.Unit.Synapses;
using DigitalBrain.Time;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Installs;

// Records every hook call as "<verb> <state>".
internal sealed class RecordingHooks : IInstallHooks, IServiceProvider
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public object? GetService(Type serviceType) => serviceType == typeof(IInstallHooks) ? this : null;

    public bool FailEdges { get; set; }

    public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, CancellationToken ct)
        => FailEdges ? throw new InvalidOperationException("edges down") : Note($"edges {state}");

    public Task RemoveReactionEdges(NeuronId state, CancellationToken ct) => Note($"unedges {state}");

    public Task RegisterSchedules(NeuronId state, IReadOnlyList<Schedule> schedules, CancellationToken ct) => Note($"schedules {state}");

    public Task UnregisterSchedules(NeuronId state, CancellationToken ct) => Note($"unschedules {state}");

    private Task Note(string call)
    {
        Calls.Enqueue(call);
        return Task.CompletedTask;
    }
}

public sealed class InstallsFacts
{
    private static readonly NeuronId Installs = NeuronId.Parse("b/synapse.installs/default");
    private static readonly Dictionary<string, string> None = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OS Start(RecordingHooks hooks) => OS.Local(o => o.Services = hooks).Register(typeof(StateNeuronOnTestOSFacts).Assembly);

    private static async Task<ConfirmationRequested> Asked(OS os, int count)
    {
        for (var i = 0; i < 500; i++)
        {
            if (os.Confirmations.Requested.Count >= count && os.Confirmations.Waiting.Count > 0) { return os.Confirmations.Waiting[0]; }
            await Task.Delay(10, Ct);
        }
        throw new TimeoutException("No install was asked.");
    }

    private static async Task<InstallOutcome> Answer(OS os, string alias, bool approve, int asked = 1)
    {
        var install = os.Get<IInstalls>(Installs).Install(new InstallRequest(alias, None), Ct);
        var request = await Asked(os, asked);
        if (approve) { await os.Confirmations.Approve(request.PlanId); }
        else { await os.Confirmations.Deny(request.PlanId); }
        return await install;
    }

    [Fact]
    public async Task InstallAsksWithTheSentenceAndRecordsOnApproval()
    {
        var hooks = new RecordingHooks();
        await using var os = Start(hooks);
        var outcome = await Answer(os, "week", approve: true);
        var needs = os.Catalog.NeedsOf("week");
        Assert.Equal(InstallStatus.Installed, outcome.Status);
        var plan = Assert.Single(os.Confirmations.Requested).Plan;
        Assert.Equal(needs.Sentence(), plan.Body);
        Assert.True(plan.RequiresPerson);
        var record = await os.Get<IInstalls>(Installs).Describe("week", Ct);
        Assert.NotNull(record);
        Assert.Equal(needs.Hash(), record.NeedsHash);
        Assert.Equal(["edges b/week/default", "schedules b/week/default"], hooks.Calls);
        Assert.Contains(os.Journal(Installs.Brain).Signals(Installs), item => item.Signal is Installed { Alias: "week" });
    }

    [Fact]
    public async Task ADeniedInstallRecordsNothing()
    {
        var hooks = new RecordingHooks();
        await using var os = Start(hooks);
        var outcome = await Answer(os, "week", approve: false);
        Assert.Equal(InstallStatus.Denied, outcome.Status);
        Assert.Null(await os.Get<IInstalls>(Installs).Describe("week", Ct));
        Assert.Empty(await os.Get<IInstalls>(Installs).List(Ct));
        Assert.Empty(hooks.Calls);
        Assert.DoesNotContain(os.Journal(Installs.Brain).Signals(Installs), item => item.Signal is Installed);
    }

    [Fact]
    public async Task ADeniedInstallCanBeAskedAgain()
    {
        await using var os = Start(new RecordingHooks());
        Assert.Equal(InstallStatus.Denied, (await Answer(os, "week", approve: false)).Status);
        Assert.Equal(InstallStatus.Installed, (await Answer(os, "week", approve: true, asked: 2)).Status);
        Assert.Equal(2, os.Confirmations.Requested.Count);
        Assert.NotNull(await os.Get<IInstalls>(Installs).Describe("week", Ct));
    }

    [Fact]
    public async Task ASecondInstallWithTheSameNeedsIsUnchangedAndSilent()
    {
        var hooks = new RecordingHooks();
        await using var os = Start(hooks);
        await Answer(os, "week", approve: true);
        var again = await os.Get<IInstalls>(Installs).Install(new InstallRequest("week", None), Ct);
        Assert.Equal(InstallStatus.Unchanged, again.Status);
        Assert.Single(os.Confirmations.Requested);
        Assert.Equal(4, hooks.Calls.Count);
    }

    [Fact]
    public async Task AChangedSignatureReprompts()
    {
        var hooks = new RecordingHooks();
        await using var os = Start(hooks);
        Assert.Equal(InstallStatus.Installed, (await Answer(os, "week", approve: true)).Status);
        Assert.Equal(InstallStatus.Installed, (await Answer(os, "ledger", approve: true, asked: 2)).Status);
        Assert.Equal(["week", "ledger"], os.Confirmations.Requested.Select(r => r.Plan.TargetLabel));
        Assert.NotEqual(os.Confirmations.Requested[0].PlanId, os.Confirmations.Requested[1].PlanId);
    }

    [Fact]
    public async Task UnknownAliasIsRefused()
    {
        await using var os = Start(new RecordingHooks());
        var outcome = await os.Get<IInstalls>(Installs).Install(new InstallRequest("nowhere", None), Ct);
        Assert.Equal(InstallStatus.Refused, outcome.Status);
        Assert.Contains("nowhere", outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(os.Confirmations.Requested);
    }

    [Fact]
    public async Task RetireRemovesEdgesAndSchedulesAndPublishesRetired()
    {
        var hooks = new RecordingHooks();
        await using var os = Start(hooks);
        await Answer(os, "week", approve: true);
        await os.Get<IInstalls>(Installs).Retire("week", Ct);
        Assert.Null(await os.Get<IInstalls>(Installs).Describe("week", Ct));
        Assert.Equal(["edges b/week/default", "schedules b/week/default", "unedges b/week/default", "unschedules b/week/default"], hooks.Calls);
        Assert.Contains(os.Journal(Installs.Brain).Signals(Installs), item => item.Signal is Retired { Alias: "week" });
    }

    [Fact]
    public async Task AFailingHookStillRecordsAndPublishesGateChanged()
    {
        await using var os = Start(new RecordingHooks { FailEdges = true });
        var outcome = await Answer(os, "week", approve: true);
        Assert.Equal(InstallStatus.Installed, outcome.Status);
        Assert.Equal("hooks failed: edges down", outcome.Reason);
        Assert.NotNull(await os.Get<IInstalls>(Installs).Describe("week", Ct));
        await os.Idle();
        Assert.Single(os.InstalledGates.GatesFor(Installs.Brain, typeof(Charging)));
    }

    [Fact]
    public async Task UnchangedPublishesGateChanged()
    {
        await using var os = Start(new RecordingHooks());
        await Answer(os, "week", approve: true);
        await os.Idle();
        var heard = new ConcurrentQueue<GateChanged>();
        await using var listen = await os.ClusterListeners.Listen<GateChanged>("fact", (_, signal, _) =>
        {
            heard.Enqueue(signal);
            return Task.CompletedTask;
        });
        Assert.Equal(InstallStatus.Unchanged, (await os.Get<IInstalls>(Installs).Install(new InstallRequest("week", None), Ct)).Status);
        await os.Idle();
        Assert.Equal([new GateChanged("b")], heard);
    }

    [Fact]
    public async Task GateIndexServesOnlyInstalledGates()
    {
        await using var os = Start(new RecordingHooks());
        Assert.Empty(os.InstalledGates.GatesFor(Installs.Brain, typeof(Charging)));
        await Answer(os, "week", approve: true);
        await os.Idle();
        var gate = Assert.Single(os.InstalledGates.GatesFor(Installs.Brain, typeof(Charging)));
        Assert.Equal(NeuronId.Parse("b/week/default"), gate.Gate);
        Assert.Empty(os.InstalledGates.GatesFor(new BrainId("other"), typeof(Charging)));
    }
}
