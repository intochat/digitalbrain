using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SliderFacts
{
    [Fact]
    public async Task ConfigureAndSetValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new FlutterModule()] }, ct);
        var slider = brain.Get<ISlider>("vol");
        await slider.Configure(0, 10, 1);
        await slider.SetValue(4);
        Assert.Equal(4, (await slider.Read()).Value);
    }
}
