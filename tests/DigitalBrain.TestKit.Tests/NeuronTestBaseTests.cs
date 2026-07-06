using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.TestKit.Tests;

public class NeuronTestBaseTests : NeuronTestBase
{
    [Fact]
    public async Task Grain_Resolves_And_Returns_A_Live_Timeline()
    {
        var target = Grain<IGeneratedNeuron>("neuron-test-base-smoke"); // Demo removed as trash
        var timeline = await target.GetTimelineAsync();
        Assert.NotNull(timeline);
    }
}
