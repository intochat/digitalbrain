using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.TestKit.Tests;

public class TestDigitalBrainTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();

    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task FireAsync_Delivers_To_Self_Addressed_Grain()
    {
        // INeuron itself has many concrete grain implementors in DigitalBrain.Kernel, so Orleans
        // can't resolve GetGrain<INeuron> to a single grain type. Using IGeneratedNeuron (Demo removed as trash).
        var target = _brain.Grain<IGeneratedNeuron>("smoke-test-neuron");
        var timeline = await target.GetTimelineAsync();
        Assert.NotNull(timeline);
    }
}
