using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using Microsoft.Playwright;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Button;

[Collection("Flutter browser")]
public sealed class ButtonWebFacts
{
    [Fact(Timeout = 240_000)]
    public async Task BackendLabelUpdatesRenderInLiveShell()
    {
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>()
            .StartAsync(TestContext.Current.CancellationToken);
        var button = brain.Get<IButton>("e2e");
        await button.Set("Fire e2e", "e2e-click");
        await Assertions.Expect(brain.Page.GetByRole(AriaRole.Button, new() { Name = "Fire e2e", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await button.Set("Updated action", "updated");
        await Assertions.Expect(brain.Page.GetByRole(AriaRole.Button, new() { Name = "Updated action", Exact = true })).ToBeVisibleAsync();
    }
}
