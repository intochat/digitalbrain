using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Button;

[Collection(FlutterHostCollection.Name)]
public sealed class ButtonHttpFacts(FlutterHostFixture host)
{
    [Fact]
    public async Task ClickPublishesClicked()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = host.Brain;
        var ws = host.Workspace();
        var button = brain.Get<IButton>(UiScope.Key(BrainScope.Create("owner", ws).Id, "go"));
        await using var clicks = await brain.Observe<ButtonClicked>(button, ct);
        using var set = await brain.HttpClient.PostAsJsonAsync($"/brains/{ws}/ui/buttons/go/set", new { label = "Open", action = "navigate:docs" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, set.StatusCode);
        using var click = await brain.HttpClient.PostAsJsonAsync($"/brains/{ws}/ui/buttons/go/click", new { }, ct);
        Assert.Equal(HttpStatusCode.Accepted, click.StatusCode);
        Assert.Equal("navigate:docs", (await clicks.NextAsync(ct: ct)).Action);
    }
}
