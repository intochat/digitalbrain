using DigitalBrain.Core;
using DigitalBrain.Time;
using Xunit;
using ITimer = DigitalBrain.Time.ITimer;

namespace DigitalBrain.Tests;

public sealed class ModuleReflectionProofs
{
    [Fact]
    public void TimeContractsReflectIntoTheTimerManifest()
    {
        var manifest = ModuleReflection.ManifestOf(typeof(ITimer).Assembly);

        var timer = Assert.Single(manifest.Neurons, neuron => neuron.ContractId == "timer");
        Assert.Equal("default", timer.DefaultInstanceName);
        Assert.Contains(timer.Accepted, synapse => synapse.ContractId == "time.start-timer");
        Assert.Contains(timer.Accepted, synapse => synapse.ContractId == "time.cancel-timer");
        Assert.Contains(timer.Emitted, synapse => synapse.ContractId == "time.timer-scheduled");
        Assert.Contains(timer.Emitted, synapse => synapse.ContractId == "time.timer-cancelled");
    }

    [Fact]
    public void ModuleVocabularyListsEveryFactIncludingUnattributedOnes()
    {
        var manifest = ModuleReflection.ManifestOf(typeof(ITimer).Assembly);

        Assert.Contains(manifest.Facts, fact => fact.ContractId == "time.timer-elapsed");
        Assert.Contains(manifest.Facts, fact => fact.ContractId == "time.timer-scheduled");
    }

    [Fact]
    public void RequestSchemasSurviveWithoutDescriptionProse()
    {
        var manifest = ModuleReflection.ManifestOf(typeof(ITimer).Assembly);

        var timer = Assert.Single(manifest.Neurons, neuron => neuron.ContractId == "timer");
        var start = Assert.Single(timer.Accepted, synapse => synapse.ContractId == "time.start-timer");

        Assert.Contains("durationSeconds", start.JsonSchema, StringComparison.Ordinal);
        Assert.Contains("note", start.JsonSchema, StringComparison.Ordinal);
        Assert.Equal("Start timer", start.Description);
    }
}
