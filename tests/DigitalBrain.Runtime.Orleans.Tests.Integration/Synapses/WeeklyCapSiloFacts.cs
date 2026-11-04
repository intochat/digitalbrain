using System.Collections.Concurrent;
using DigitalBrain.Framework.Tests.E2E.WeeklyCap;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// The four WeeklyCapFacts scenarios of the TestOS end-to-end suite, replayed on the silo (the sample files are linked from there).
// One class per scenario, so each runs on its own silo and the week it spends is its own.
public abstract class WeeklyCapSiloBase(StatefulSilo silo)
{
    protected static readonly NeuronId Pay = NeuronId.Parse("wc/payments/p");
    protected static readonly NeuronId Week = NeuronId.Parse("wc/week/default");
    protected static readonly NeuronId Audit = NeuronId.Parse("wc/audit/default");

    protected StatefulSilo Silo { get; } = silo;

    protected IPayments Payments => Silo.Get<IPayments>(Pay.ToString());

    protected async Task<decimal> Spent()
    {
        var host = Silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, Week.ToString()));
        var state = await host.InspectState(TestContext.Current.CancellationToken);
        return state!.Value.GetProperty("spent").GetDecimal();
    }

    protected async Task<int> AuditCount()
        => (await Silo.History("wc").Read(Audit, 0, 100, TestContext.Current.CancellationToken)).Signals.Count(s => s.Signal is StateChanged);

    protected Task SpentReaches(decimal expected)
        => StateNeuronSiloFacts.Until(async () => await Spent() == expected, TestContext.Current.CancellationToken);
}

public sealed class WeeklyCapSiloFacts(StatefulSilo silo) : WeeklyCapSiloBase(silo), IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task ThePromiseHoldsAcrossAWeekOfCharges()
    {
        await Payments.Charge(120m);
        await Payments.Charge(100m);
        await Payments.Charge(70m);
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => Payments.Charge(20m));

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
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

public sealed class WeeklyCapSiloTwoGateFacts(StatefulSilo silo) : WeeklyCapSiloBase(silo), IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task TwoGatesOnOneSignalStopAtTheFirstRefusal()
    {
        await Payments.Charge(250m);
        await SpentReaches(250m);
        var auditedBefore = await AuditCount();
        Assert.Equal(1, auditedBefore);

        await Assert.ThrowsAsync<RuleViolationException>(() => Payments.Charge(100m));

        // Holds whatever the gate order: Committed runs only when every gate allowed, so the refused charge leaves no audit entry.
        Assert.Equal(auditedBefore, await AuditCount());
        Assert.Equal(250m, await Spent());

        await Payments.Charge(10m);
        await SpentReaches(260m);
        await StateNeuronSiloFacts.Until(async () => await AuditCount() == auditedBefore + 1, TestContext.Current.CancellationToken);
        Assert.Equal(auditedBefore + 1, await AuditCount());
    }
}

public sealed class WeeklyCapSiloThrowingGateFacts(StatefulSilo silo) : WeeklyCapSiloBase(silo), IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task AThrowingGateFailsClosedWithItsName()
    {
        var ct = TestContext.Current.CancellationToken;
        await Payments.Charge(10m);
        await SpentReaches(10m);

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => Payments.ChargeRisky(5m));

        Assert.EndsWith(".Broken", e.Rejected.Rules[0]);
        Assert.DoesNotContain((await Silo.History("wc").Read(NeuronId.Parse("wc/broken/default"), 0, 100, ct)).Signals, s => s.Signal is StateChanged);
        Assert.DoesNotContain((await Silo.History("wc").Read(Pay, 0, 100, ct)).Signals, s => s.Signal is ChargingRisky);
        Assert.Equal(10m, await Spent());
    }
}

public sealed class WeeklyCapSiloInspectorFacts(StatefulSilo silo) : WeeklyCapSiloBase(silo), IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task TheInspectorHearsStartedAndFinishedAroundEachGate()
    {
        var started = new ConcurrentQueue<SynapseStarted>();
        var finished = new ConcurrentQueue<SynapseFinished>();
        var listeners = Silo.Services.GetRequiredService<IClusterListeners>();
        await using var startedListener = await listeners.Listen<SynapseStarted>("wc-inspector-started", (_, s, _) =>
        {
            started.Enqueue(s);
            return Task.CompletedTask;
        });
        await using var finishedListener = await listeners.Listen<SynapseFinished>("wc-inspector-finished", (_, f, _) =>
        {
            finished.Enqueue(f);
            return Task.CompletedTask;
        });

        await Payments.Charge(10m);
        await StateNeuronSiloFacts.Until(() => Task.FromResult(finished.Any(f => f.Function.EndsWith(".WeeklyCap", StringComparison.Ordinal))), TestContext.Current.CancellationToken);

        Assert.Single(started, s => s.Function.EndsWith(".WeeklyCap", StringComparison.Ordinal));
        var done = Assert.Single(finished, f => f.Function.EndsWith(".WeeklyCap", StringComparison.Ordinal));
        Assert.Equal("allowed", done.Outcome);
    }
}
