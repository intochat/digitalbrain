using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Slider;

public sealed class SliderFacts
{
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