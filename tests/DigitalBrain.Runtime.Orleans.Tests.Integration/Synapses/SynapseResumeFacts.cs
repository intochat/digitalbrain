using System.Text.RegularExpressions;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// 04 D-04, D-12, R4-02, contract "Write-then-crash window": synapses survive deactivation and silo restart.
public sealed partial class SynapseResumeFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    [Fact]
    public async Task After_each_of_three_reactivations_the_signal_arrives_once_and_the_subscription_count_stays_one()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/reactivate", Source = "b1/probe.counter/reactivate";
        await silo.Get<IListener>(Listener).Follow(Source);
        for (var round = 1; round <= 3; round++)
        {
            await silo.DeactivateAll();
            await silo.Get<ICounter>(Source).Add(1);
            await Heard.Until(Listener, round, ct);
            await Task.Delay(300, ct);
            Assert.Equal(round, Heard.Of(Listener).Length);
            Assert.Equal(1, await silo.Subscriptions(Listener, "probe.counted"));
        }
        Assert.Equal(["counted:1", "counted:2", "counted:3"], Heard.Of(Listener));
    }

    [Fact]
    public async Task A_signal_appended_without_a_reference_is_caught_up_when_the_subscriber_activates()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/activate", Source = "b1/probe.counter/activate";
        await silo.Get<IListener>(Listener).Follow(Source);
        await silo.DeactivateAll();
        await KillBetweenAppendAndPublish(Source);

        Assert.Equal(1, await silo.Get<IListener>(Listener).Ping());
        Assert.Equal(["counted:1"], Heard.Of(Listener)); // caught up inside activation, before the call returned
    }

    [Fact]
    public async Task Reconcile_bounds_its_catch_ups_and_two_runs_at_once_deliver_each_signal_once()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/reconcile", Source = "b1/probe.counter/reconcile";
        await silo.Get<IListener>(Listener).Follow(Source);
        await KillBetweenAppendAndPublish(Source);
        Assert.Empty(Heard.Of(Listener));

        var reconcile = silo.Services.GetRequiredService<SynapseReconcile>();
        await Task.WhenAll(reconcile.Run(ct), reconcile.Run(ct));
        await Heard.Until(Listener, 1, ct);
        await Task.Delay(500, ct);
        Assert.Equal(["counted:1"], Heard.Of(Listener));
        Assert.Equal(64, SynapseReconcile.MaxConcurrentCatchUps);
    }

    [Fact]
    public void No_observer_or_lease_code_exists_in_the_digitalbrain_sources()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "DigitalBrain.slnx"))) { root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("No repository root."); }
        var hits = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Forbidden().IsMatch(File.ReadAllText(path)))
            .ToList();
        Assert.Empty(hits);
    }

    private async Task KillBetweenAppendAndPublish(string source)
    {
        silo.Faults.FailAfterAppend = true;
        try { await Assert.ThrowsAnyAsync<Exception>(() => silo.Get<ICounter>(source).Add(1)); }
        finally { silo.Faults.FailAfterAppend = false; }
    }

    [GeneratedRegex(@"\b(WatchRenewing|ObserverManager|INeuronObserver)\b")]
    private static partial Regex Forbidden();
}

// A silo restart: subscriptions persist in the file PubSubStore and reconcile wakes subscribers that are behind.
public sealed class SynapseRestartFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    [Fact]
    public async Task A_signal_appended_before_a_restart_without_a_reference_reaches_the_subscriber_once_after_restart_through_reconcile()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/restart", Source = "b1/probe.counter/restart";
        await silo.Get<IListener>(Listener).Follow(Source);
        silo.Faults.FailAfterAppend = true;
        try { await Assert.ThrowsAnyAsync<Exception>(() => silo.Get<ICounter>(Source).Add(1)); }
        finally { silo.Faults.FailAfterAppend = false; }
        Assert.Empty(Heard.Of(Listener));

        await silo.Restart(ct);
        await Heard.Until(Listener, 1, ct, seconds: 30);
        Assert.Equal(["counted:1"], Heard.Of(Listener));
        Assert.Equal(1, await silo.Subscriptions(Listener, "probe.counted"));

        // A live signal after the restart arrives through the resumed subscription, once.
        await silo.Get<ICounter>(Source).Add(1);
        await Heard.Until(Listener, 2, ct);
        await Task.Delay(300, ct);
        Assert.Equal(2, Heard.Of(Listener).Length);
        Assert.Equal(1, await silo.Subscriptions(Listener, "probe.counted"));
    }
}
