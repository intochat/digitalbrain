using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using DigitalBrain.Testing.Tests.Unit.Host;
using OS = DigitalBrain.Testing.TestOS;
using Xunit;

namespace DigitalBrain.Testing.Tests.Unit.Host;

public sealed class AliasClaimSurvivesRestartFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    [Fact]
    public async Task AliasClaimedBySourceSurvivesRestartAndRefusesRival()
    {
        await using var os = OS.Local();
        var brain = Brain("claim-test");

        // 1. Deploy source A claiming alias "hs.week"
        var outcomeA = await os.Synapses.Deploy(brain, HsSource.Of(HsSource.Budget), new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, outcomeA.Status);
        await os.Idle();

        // 2. Restart TestOS
        await os.Restart();

        // 3. Verify ISources still records ownership
        var sources = os.Get<ISources>(NeuronId.Parse($"{brain}/synapse.sources/default"));
        Assert.Equal(HsSource.Key, await sources.OwnerOf("hs.week", Ct));

        // 4. Attempt to deploy source B (Rival) declaring the same alias "hs.week"
        var rivalSource = HsSource.Of(HsSource.Rival, "Me.Rival");
        var outcomeB = await os.Synapses.Deploy(brain, rivalSource, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);

        // 5. Must be refused due to alias ownership conflict
        Assert.Equal(DeployStatus.Refused, outcomeB.Status);
        Assert.Contains("hs.week", outcomeB.Reason);
        Assert.Contains(HsSource.Key, outcomeB.Reason);
    }
}
