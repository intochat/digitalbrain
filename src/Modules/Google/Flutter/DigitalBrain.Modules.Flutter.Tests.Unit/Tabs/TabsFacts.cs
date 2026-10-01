using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Tabs;

public sealed class TabsFacts
{
    [Fact]
    public async Task SetThenSelect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var tabs = brain.Get<ITabs>("pages");
        await tabs.Set(
            [new TabItem("a", "A", new UiChildRef("text", "about")), new TabItem("b", "B", new UiChildRef("chart", "btc"))],
            "a");
        await tabs.Select("b");
        Assert.Equal("b", (await tabs.Read()).SelectedId);
    }
}
