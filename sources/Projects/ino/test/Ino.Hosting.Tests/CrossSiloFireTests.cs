using Ino.Kernel.Contracts;
using Ino.Testing;
using Orleans;
using Xunit;

namespace Ino.Hosting.Tests;

/// <summary>
/// L3 scenario 4: cluster with SystemEcho grain — fire EchoRequest via
/// GrainFactory.GetGrain routes to SystemEcho regardless of which silo the
/// caller lives on. Proves Orleans' location transparency underwrites
/// FirePort's cross-silo routing.
/// </summary>
[Collection(nameof(InoMultiSiloCollection))]
public sealed class CrossSiloFireTests(InoMultiSiloFixture fx)
{
    [Fact]
    public async Task SystemEcho_resolves_and_handles_across_silos()
    {
        var grain = fx.Cluster.GrainFactory.GetGrain<Ino.Core.Hosting.INeuron<EchoRequest>>(
            primaryKey: Guid.NewGuid().ToString());

        var ctx = TestNeuronContext.New();
        var result = await grain.HandleAsync(new EchoRequest("hello"), ctx, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.TryGetPayload<EchoResponse>(out var resp));
        Assert.Contains("[from system]", resp!.Message);
        Assert.Contains("hello", resp.Message);
        // SystemEcho stamps its RuntimeIdentity — needed to verify cross-silo dispatch below.
        Assert.False(string.IsNullOrEmpty(resp.SiloAddress));
    }

    /// <summary>
    /// Iterates many unique correlation keys and asserts that SystemEcho
    /// activations end up on both silos. Since all calls originate from the
    /// same external cluster-client, activations that respond from the
    /// non-primary silo *must* have crossed a silo boundary. This is the
    /// real "cross-silo" proof the previous single-call test did not give.
    /// </summary>
    [Fact]
    public async Task SystemEcho_activations_span_both_silos_under_random_placement()
    {
        var distinctSilos = new HashSet<string>(StringComparer.Ordinal);
        var ctx = TestNeuronContext.New();

        for (var i = 0; i < 40 && distinctSilos.Count < 2; i++)
        {
            var grain = fx.Cluster.GrainFactory.GetGrain<Ino.Core.Hosting.INeuron<EchoRequest>>(
                primaryKey: Guid.NewGuid().ToString());

            var result = await grain.HandleAsync(new EchoRequest($"probe-{i}"), ctx, TestContext.Current.CancellationToken);
            Assert.True(result.TryGetPayload<EchoResponse>(out var resp));
            if (resp!.SiloAddress is not null)
                distinctSilos.Add(resp.SiloAddress);
        }

        // SystemEcho must be dispatched across both silos in a 2-silo cluster — otherwise the test does not prove cross-silo routing.
        Assert.True(distinctSilos.Count >= 2);
    }
}
