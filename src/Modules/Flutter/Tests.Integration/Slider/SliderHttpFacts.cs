using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SliderHttpFacts
{
    [Fact]
    public async Task GetMatchesValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
            new() { Modules = [FlutterModule.Define(new() { Hosting = new() { Kind = FlutterHostKind.None } })] }, ct);
        var slider = brain.Get<ISlider>("vol");
        await slider.Configure(0, 10, 1);
        await slider.SetValue(4);
        var state = await brain.HttpClient.GetFromJsonAsync<SliderState>("/ui/sliders/vol", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal(4, state!.Value);
    }
}
