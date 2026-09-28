using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Button;

public sealed class ButtonHttpFacts
{
    [Fact]
    public async Task ClickPublishesClicked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        var button = brain.Get<IButton>(UiScope.Key("workspace-a", "go"));
        await using var clicks = await brain.Observe<ButtonClicked>(button, ct);
        using var set = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/ui/buttons/go/set", new { label = "Open", action = "navigate:docs" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, set.StatusCode);
        using var click = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/ui/buttons/go/click", new { }, ct);
        Assert.Equal(HttpStatusCode.Accepted, click.StatusCode);
        Assert.Equal("navigate:docs", (await clicks.NextAsync(ct: ct)).Action);
    }
}