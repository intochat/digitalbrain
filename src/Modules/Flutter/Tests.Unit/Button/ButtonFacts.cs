using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ButtonFacts
{
    [Fact]
    public async Task ClickPublishesAction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(FlutterModule))] }, ct);
        var button = brain.Get<IButton>("go");
        await using var clicks = await brain.Observe<ButtonClicked>(button, ct);
        await button.Set("Open", "navigate:docs");
        await button.Click();
        Assert.Equal("navigate:docs", (await clicks.NextAsync(ct: ct)).Action);
        Assert.Equal(1, (await button.Read()).ClickCount);
    }
}
