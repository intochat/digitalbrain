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

    public Task<string?> ValidateBinding(NeuronId state, SlotDescriptor slot, string binding, CancellationToken ct) => Task.FromResult<string?>(null);

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

    // Install returns Pending with the plan id at once; the person answers; the resolution completes the install.
    private static async Task<InstallRecord?> Answer(OS os, string alias, bool approve)
    {
        var outcome = await os.Get<IInstalls>(Installs).Install(new InstallRequest(alias, None), Ct);
        Assert.Equal(InstallStatus.Pending, outcome.Status);
        var request = Assert.Single(os.Confirmations.Waiting, w => w.PlanId == outcome.Reason);
        if (approve) { await os.Confirmations.Approve(request.PlanId); }
        else { await os.Confirmations.Deny(request.PlanId); }
        await os.Idle();
        return await os.Get<IInstalls>(Installs).Describe(alias, Ct);
    }

    [Fact]
    public async Task InstallAsksWithTheSentenceAndRecordsOnApproval()
    {
        var hooks = new RecordingHooks();
        await using var os = Start(hooks);
        var record = await Answer(os, "week", approve: true);
        var needs = os.Catalog.NeedsOf("week");
        var plan = Assert.Single(os.Confirmations.Requested).Plan;
        Assert.Equal(needs.Sentence(), plan.Body);
        Assert.True(plan.RequiresPerson);
        Assert.NotNull(record);
        Assert.Empty(await os.Get<IInstalls>(Installs).Pending(Ct));
        Assert.Equal(needs.Hash(), record.NeedsHash);
        Assert.Equal(["edges b/week/default", "schedules b/week/default"], hooks.Calls);
        Assert.Contains(os.Journal(Installs.Brain).Signals(Installs), item => item.Signal is Installed { Alias: "week" });
    }

    [Fact]
    public async Task ADeniedInstallRecordsNothing()
    {
        var hooks = new RecordingHooks();
        await using var os = Start(hooks);
        Assert.Null(await Answer(os, "week", approve: false));
        Assert.Empty(await os.Get<IInstalls>(Installs).List(Ct));
        Assert.Empty(await os.Get<IInstalls>(Installs).Pending(Ct));
        Assert.Empty(hooks.Calls);
        Assert.DoesNotContain(os.Journal(Installs.Brain).Signals(Installs), item => item.Signal is Installed);
        Assert.Contains(os.Journal(Installs.Brain).Signals(Installs), item => item.Signal is InstallDenied { Alias: "week", Reason: "ByPerson" });
    }

    [Fact]
    public async Task ADeniedInstallCanBeAskedAgain()
    {
        await using var os = Start(new RecordingHooks());
        Assert.Null(await Answer(os, "week", approve: false));
        Assert.NotNull(await Answer(os, "week", approve: true));
        Assert.Equal(2, os.Confirmations.Requested.Count);
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
        Assert.NotNull(await Answer(os, "week", approve: true));
        Assert.NotNull(await Answer(os, "ledger", approve: true));
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
    public async Task RetireDuringAPendingAskCancelsIt()
    {
        await using var os = Start(new RecordingHooks());
        var outcome = await os.Get<IInstalls>(Installs).Install(new InstallRequest("week", None), Ct);
        Assert.Equal(InstallStatus.Pending, outcome.Status);

        await os.Get<IInstalls>(Installs).Retire("week", Ct);
        await os.Confirmations.Approve(outcome.Reason!);
        await os.Idle();

        Assert.Null(await os.Get<IInstalls>(Installs).Describe("week", Ct));
        Assert.Empty(await os.Get<IInstalls>(Installs).Pending(Ct));
        Assert.Contains(os.Journal(Installs.Brain).Signals(Installs),
            item => item.Signal is InstallDenied { Alias: "week", Reason: "retired" } denied && denied.PlanId == outcome.Reason);
    }

    [Fact]
    public async Task AFailingHookStillRecords()
    {
        await using var os = Start(new RecordingHooks { FailEdges = true });
        Assert.NotNull(await Answer(os, "week", approve: true));
        // The next install of the same needs runs the hooks again and reports their failure.
        var again = await os.Get<IInstalls>(Installs).Install(new InstallRequest("week", None), Ct);
        Assert.Equal(InstallStatus.Unchanged, again.Status);
        Assert.Equal("hooks failed: edges down", again.Reason);
    }
}
