using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Slider;

public sealed class SliderFacts
{
    [Fact]
    public async Task AnotherBrainCannotReadOrChangeTheFirstBrainsSlider()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var first = brain.Get<ISlider>(UiScope.Key("first-brain", "volume"));
        var second = brain.Get<ISlider>(UiScope.Key("second-brain", "volume"));
        await first.Configure(0, 10, 1);
        await first.SetValue(7);
        Assert.Equal(0, (await second.Read()).Value);
        await second.Configure(0, 10, 1);
        await second.SetValue(3);
        Assert.Equal(7, (await first.Read()).Value);
    }

    [Fact]
    public async Task ConfigureAndSetValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var slider = brain.Get<ISlider>("vol");
        await slider.Configure(0, 10, 1);
        await slider.SetValue(4);
        Assert.Equal(4, (await slider.Read()).Value);
    }
}
