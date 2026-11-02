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

    [GeneratedRegex(@"\b(WatchRenewing|ObserverManager|INeuronObserver)\b")]
    private static partial Regex Forbidden();
}
