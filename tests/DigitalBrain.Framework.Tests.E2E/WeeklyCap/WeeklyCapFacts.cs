using System.Collections.Concurrent;
using DigitalBrain.Rules;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Framework.Tests.E2E.WeeklyCap;

public sealed class WeeklyCapFacts
{
    private static readonly NeuronId Pay = NeuronId.Parse("b/payments/p");
    private static readonly NeuronId Week = NeuronId.Parse("b/week/default");
    private static readonly NeuronId Audit = NeuronId.Parse("b/audit/default");

    // Gates and reactions come from the install: every fact starts with the week, the audit and the broken gate installed.
    private static async Task<OS> Start()
    {
        var os = OS.Local().Register(typeof(WeeklyCapFacts).Assembly);
        os.Confirmations.ApproveAll();
        var installs = os.Get<IInstalls>(NeuronId.Parse("b/synapse.installs/default"));
        foreach (var alias in new[] { "week", "audit", "broken" })
        {
            await installs.Install(new(alias, new Dictionary<string, string>()), TestContext.Current.CancellationToken);
        }
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
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => pay.Charge(20m));
        await os.Idle();

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
        Assert.Equal(290m, await Spent(os));

        await pay.Roll(new DateOnly(2026, 11, 9));
        await os.Idle();
        Assert.Equal(0m, await Spent(os));

        await pay.Charge(20m);
        await os.Idle();
        Assert.Equal(20m, await Spent(os));
    }

    [Fact]
    public async Task TwoGatesOnOneSignalStopAtTheFirstRefusal()
    {
        await using var os = await Start();
        var pay = os.Get<IPayments>(Pay);
        await pay.Charge(250m);
        await os.Idle();
        var auditedBefore = os.Journal(Pay.Brain).Signals(Audit).Count(s => s.Signal is StateChanged);
        Assert.Equal(1, auditedBefore);

        await Assert.ThrowsAsync<RuleViolationException>(() => pay.Charge(100m));
        await os.Idle();

        // Holds whatever the gate order: Committed runs only when every gate allowed, so the refused charge leaves no audit entry.
        var audited = os.Journal(Pay.Brain).Signals(Audit).Where(s => s.Signal is StateChanged).ToList();
        Assert.Equal(auditedBefore, audited.Count);
        Assert.Equal(250m, await Spent(os));

        await pay.Charge(10m);
        await os.Idle();
        Assert.Equal(auditedBefore + 1, os.Journal(Pay.Brain).Signals(Audit).Count(s => s.Signal is StateChanged));
    }

    [Fact]
    public async Task AThrowingGateFailsClosedWithItsName()
    {
        await using var os = await Start();
        var pay = os.Get<IPayments>(Pay);
        await pay.Charge(10m);
        await os.Idle();

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => pay.ChargeRisky(5m));
        await os.Idle();

        Assert.EndsWith(".Broken", e.Rejected.Rules[0]);
        Assert.DoesNotContain(os.Journal(Pay.Brain).Signals(NeuronId.Parse("b/broken/default")), s => s.Signal is StateChanged);
        Assert.DoesNotContain(os.Journal(Pay.Brain).Signals(Pay), s => s.Signal is ChargingRisky);
        Assert.Equal(10m, await Spent(os));
    }

    [Fact]
    public async Task TheInspectorHearsStartedAndFinishedAroundEachGate()
    {
        await using var os = await Start();
        var started = new ConcurrentQueue<SynapseStarted>();
        var finished = new ConcurrentQueue<SynapseFinished>();
        await using var startedListener = await os.ClusterListeners.Listen<SynapseStarted>("inspector-started", (_, s, _) =>
        {
            started.Enqueue(s);
            return Task.CompletedTask;
        });
        await using var finishedListener = await os.ClusterListeners.Listen<SynapseFinished>("inspector-finished", (_, f, _) =>
        {
            finished.Enqueue(f);
            return Task.CompletedTask;
        });

        await os.Get<IPayments>(Pay).Charge(10m);
        await os.Idle();

        Assert.Single(started, s => s.Function.EndsWith(".WeeklyCap", StringComparison.Ordinal));
        var done = Assert.Single(finished, f => f.Function.EndsWith(".WeeklyCap", StringComparison.Ordinal));
        Assert.Equal("allowed", done.Outcome);
    }
}
