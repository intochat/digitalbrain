using System.Collections.Concurrent;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

[Signal("test.sb.charging")]
public sealed record SbCharging(decimal Amount) : Signal;

[State("sb.week")]
public sealed record SbWeekSpend(decimal Spent)
{
    public SbWeekSpend() : this(0m) { }
}

[State("sb.ledger")]
public sealed record SbLedger(decimal Spent)
{
    public SbLedger() : this(0m) { }
}

public sealed record SbPayState
{
    public int Count { get; init; }
}

public static class SbBudget
{
    [Synapse(typeof(SbCharging), "sb.week")]
    public static SbWeekSpend WeeklyCap(SbCharging s, SbWeekSpend state) =>
        state with { Spent = state.Spent + s.Amount };

    [Synapse("sb.week")]
    public static DigitalBrain.UI.View Show(SbWeekSpend w) => new("sb.week", new DigitalBrain.UI.Text($"{w.Spent} of 300"));
}

public static class SbBooks
{
    [Synapse(typeof(SbCharging), "sb.ledger")]
    public static SbLedger Add(SbCharging s, SbLedger w) => w with { Spent = w.Spent + s.Amount };
}

[Signal("test.sb.poke")]
public sealed record SbPoke(bool Boom) : Signal;

[State("sb.fragile")]
public sealed record SbFragile(int Value)
{
    public SbFragile() : this(0) { }
}

[State("sb.echo")]
public sealed record SbEcho(int Value)
{
    public SbEcho() : this(0) { }
}

public static class SbFragileUnit
{
    [Synapse(typeof(SbPoke), "sb.fragile")]
    public static SbFragile Count(SbPoke p, SbFragile c) => p.Boom ? throw new InvalidOperationException("boom") : c with { Value = c.Value + 1 };

    [Synapse(typeof(SynapseFailed), "sb.echo")]
    public static SbEcho Echo(SynapseFailed f, SbEcho e) => throw new InvalidOperationException("echo");

    [Synapse(typeof(SbPoke), "sb.echo")]
    public static SbEcho Heard(SbPoke p, SbEcho e) => e with { Value = e.Value + 1 };
}

public interface ISbPoker
{
    Task Poke(bool boom);
}

[Neuron("sb.poker")]
public sealed class SbPoker : Neuron<SbFragile>, ISbPoker
{
    public Task Poke(bool boom) => Save(State with { Value = State.Value + 1 }, new SbPoke(boom));
}

public interface ISbPayments
{
    Task Charge(decimal amount);
}

[Neuron("sb.payments")]
public sealed class SbPayments : Neuron<SbPayState>, ISbPayments
{
    public Task Charge(decimal amount) => Save(State with { Count = State.Count + 1 }, new SbCharging(amount));
}

public sealed class StateNeuronSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    internal static async Task<decimal> SpentOf(IServiceProvider services, NeuronId id, decimal? expected = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var host = services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, id.ToString()));
        decimal? spent = null;
        await Until(async () =>
        {
            var state = await host.InspectState(ct);
            if (state is { } json && json.TryGetProperty("spent", out var prop))
            {
                spent = prop.GetDecimal();
                return expected is null || spent == expected;
            }
            return false;
        }, ct);
        return spent ?? throw new InvalidOperationException($"No state recorded for {id}");
    }

    [Fact]
    public async Task ViewChangedIsHeardAfterAnAllowedCharge()
    {
        await Install(silo.Services, "sb", "sb.week");
        var ct = TestContext.Current.CancellationToken;
        var heard = new ConcurrentQueue<string>();
        await using var listener = await silo.Services.GetRequiredService<IClusterListeners>().Listen<ViewChanged>("views", (_, changed, _) =>
        {
            heard.Enqueue(changed.Alias);
            return Task.CompletedTask;
        });

        await silo.Get<ISbPayments>("sb/sb.payments/view").Charge(1m);
        await Until(() => Task.FromResult(heard.Contains("sb.week")), ct);

        Assert.Contains("sb.week", heard);
    }

    [Fact]
    public async Task AFailedReactionIsRecordedOnceAndTheNextSignalStillRunsOnTheSilo()
    {
        await Install(silo.Services, "sbf", "sb.fragile", "sb.echo");
        var ct = TestContext.Current.CancellationToken;
        var poker = silo.Get<ISbPoker>("sbf/sb.poker/a");

        await poker.Poke(true);
        await poker.Poke(false);
        await Until(async () => await ValueOf("sbf/sb.fragile/default", ct) == 1 && await ValueOf("sbf/sb.echo/default", ct) == 2, ct);

        Assert.Equal(1, await ValueOf("sbf/sb.fragile/default", ct));
        Assert.Equal(2, await ValueOf("sbf/sb.echo/default", ct));
        Assert.Equal(1, (await silo.History("sbf").Read(NeuronId.Parse("sbf/sb.fragile/default"), 0, 100, ct)).Signals.Count(s => s.Signal is SynapseFailed));
    }

    private async Task<int> ValueOf(string id, CancellationToken ct)
    {
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, id));
        var state = await host.InspectState(ct);
        return state is { } json && json.TryGetProperty("value", out var value) ? value.GetInt32() : 0;
    }

    // Reaction edges come from the install: each fact installs the aliases it charges in its brain (approved by the fixture).
    internal static async Task Install(IServiceProvider services, string brain, params string[] aliases)
    {
        var installs = NeuronHosting.Get<IInstalls>(services, NeuronId.Parse($"{brain}/synapse.installs/default"));
        foreach (var alias in aliases)
        {
            var outcome = await installs.Install(new(alias, new Dictionary<string, string>()), TestContext.Current.CancellationToken);
            Assert.True(outcome.Status is InstallStatus.Pending or InstallStatus.Unchanged, outcome.Reason);
            // The fixture approves at once; the installs neuron records on the resolution's reaction.
            await Until(async () => await installs.Describe(alias, TestContext.Current.CancellationToken) is not null, TestContext.Current.CancellationToken);
            Assert.NotNull(await installs.Describe(alias, TestContext.Current.CancellationToken));
        }
    }

    internal static async Task Until(Func<Task<bool>> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition() && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
    }
}
