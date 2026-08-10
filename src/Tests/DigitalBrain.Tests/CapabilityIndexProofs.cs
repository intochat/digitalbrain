using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CapabilityIndexProofs
{
    private static CapabilityIndex Index()
        => CapabilityIndex.Build(
        [
            ModuleReflection.ManifestOf(typeof(ISynapseGraph).Assembly),
            ModuleReflection.ManifestOf(typeof(StartTimer).Assembly),
        ]);

    [Fact]
    public void NaturalIntentFindsTheTimerRequest()
    {
        var hits = Index().Find("set a timer for 30 seconds", 8);

        Assert.Contains(hits, hit => hit.ContractId == "time.start-timer");
    }

    [Fact]
    public void RoutingIntentFindsTheGraphVerbs()
    {
        var hits = Index().Find("connect a source to a target", 8);

        Assert.Contains(hits, hit => hit.ContractId == "db.connect");
    }

    [Fact]
    public void RequestHitsCarryTheReflectedSignature()
    {
        var hits = Index().Find("start a timer", 8);

        var start = hits.First(hit => hit.ContractId == "time.start-timer");
        Assert.Contains("durationSeconds: int", start.Signature, StringComparison.Ordinal);
        Assert.Contains("note: string", start.Signature, StringComparison.Ordinal);
        Assert.Contains("TimerScheduled", start.Signature, StringComparison.Ordinal);
        Assert.Equal("timer", start.NeuronContractId);
        Assert.Equal("default", start.DefaultInstanceName);
    }

    [Fact]
    public void UnattributedFactsAreFindableForWiring()
    {
        var hits = Index().Find("timer elapsed", 8);

        Assert.Contains(hits, hit => hit.ContractId == "time.timer-elapsed");
    }

    [Fact]
    public void UnrelatedIntentReturnsNothingRatherThanNoise()
    {
        Assert.Empty(Index().Find("quarterly weather forecast", 8));
    }
}
