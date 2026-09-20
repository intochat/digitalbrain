using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Toggle;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ToggleFacts
{
    [Fact]
    public async Task SetAndFlipUpdateOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new FlutterModule()] }, ct);
        var toggle = brain.Get<IToggle>("dark");
        await toggle.Set("Dark", true);
        Assert.True((await toggle.Read()).On);
        await toggle.Flip();
        Assert.False((await toggle.Read()).On);
    }
}
