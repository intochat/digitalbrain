using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Testing;
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
        await using var brain = await E2ETest.StartAsync<Projects.IntoChat_AppHost>(

    new DigitalBrainConfiguration
    {
        Flutter = new() { Hosting = new() { Kind = FlutterHostKind.None } },
    }.Modules,
        new TestExecutionOptions
            {
                Browser = new() { SlowMoMilliseconds = 250 },
            },
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
}
