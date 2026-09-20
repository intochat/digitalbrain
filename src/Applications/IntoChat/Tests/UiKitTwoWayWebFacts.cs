using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Flutter.Expander;
using DigitalBrain.Flutter.Expander.Signals;
using Microsoft.Playwright;

namespace IntoChat.Tests;

public sealed class UiKitTwoWayWebFacts
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact(Timeout = 240_000)]
    public async Task NeuronChangeShowsInUiAndTapUpdatesNeuron()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2EDigitalBrainSimulation.StartAsync<Projects.IntoChat_AppHost>(
            new IntoChatOptions { Flutter = new() { Hosting = new() { Kind = FlutterHostKind.Web } } },
            new TestExecutionOptions { ArtifactDirectory = Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", "ui-kit-two-way") },
            ct);

        var expander = brain.Get<IExpander>("e2e");
        var button = brain.Get<IButton>("e2e");
        await using var expanded = await brain.Observe<ExpanderChanged>(expander, ct);
        await using var clicks = await brain.Observe<ButtonClicked>(button, ct);

        using var collapse = await brain.HttpClient.PostAsJsonAsync("/ui/expanders/e2e",
            new { header = "More", expanded = false, children = Array.Empty<object>() }, ct);
        Assert.Equal(HttpStatusCode.Accepted, collapse.StatusCode);
        Assert.False((await expanded.NextAsync(ct: ct)).Expanded);

        using var arm = await brain.HttpClient.PostAsJsonAsync("/ui/buttons/e2e/set",
            new { label = "Fire e2e", action = "e2e-click" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, arm.StatusCode);

        await using var session = await brain.OpenBrowserAsync(ct);
        await EnableSemanticsAsync(session.Page);

        await Assertions.Expect(session.Page.GetByText("Collapsed")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(session.Page.GetByText("Fire e2e")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await session.Page.GetByText("Collapsed").ClickAsync();
        Assert.True((await expanded.NextAsync(ct: ct)).Expanded);
        Assert.True((await expander.Read()).Expanded);
        await Assertions.Expect(session.Page.GetByText("Expanded")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await session.Page.GetByText("Fire e2e").ClickAsync();
        Assert.Equal("e2e-click", (await clicks.NextAsync(ct: ct)).Action);
        Assert.Equal(1, (await button.Read()).ClickCount);
        var http = await brain.HttpClient.GetFromJsonAsync<ButtonState>("/ui/buttons/e2e", Json, ct);
        Assert.Equal(1, http!.ClickCount);
    }

    private static async Task EnableSemanticsAsync(IPage page)
    {
        var semanticsUrl = page.Url + (page.Url.Contains('?') ? "&" : "?") + "semantics=true";
        await page.GotoAsync(semanticsUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var placeholder = page.Locator("flt-semantics-placeholder");
        await placeholder.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 60_000 });
        await placeholder.EvaluateAsync("element => element.click()");
        await page.Locator("flt-semantics").First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 30_000 });
    }
}
