using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.InfoBar;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.InfoBar;

public sealed class InfoBarHttpFacts
{
    [Fact]
    public async Task GetMatchesShow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IInfoBar>("warn").Show("warning", "Heads up", "disk");
        var state = await brain.HttpClient.GetFromJsonAsync<InfoBarState>("/ui/infobars/warn", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.True(state!.Visible);
    }
}