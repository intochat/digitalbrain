using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Expander;
using Microsoft.Playwright;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Expander;

[Collection("Flutter browser")]
public sealed class ExpanderWebFacts
{
    [Fact(Timeout = 240_000)]
    public async Task BackendCollapseRendersAndTapShowsExpandedState()
    {
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.RunWebApp())
            .StartAsync(TestContext.Current.CancellationToken);
        await brain.Get<IExpander>("e2e").Set("More", false, []);
        await Assertions.Expect(brain.Page.GetByRole(AriaRole.Button, new() { Name = "Collapsed More" }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await brain.Page.GetByRole(AriaRole.Button, new() { Name = "Collapsed More" }).ClickAsync();
        await Assertions.Expect(brain.Page.GetByRole(AriaRole.Button, new() { Name = "Expanded More" })).ToBeVisibleAsync();
    }
}