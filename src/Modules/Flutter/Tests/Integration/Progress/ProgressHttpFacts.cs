using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Progress;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Integration.Progress;

public sealed class ProgressHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IProgress>("load").Set(true, 0.4, "loading");
        var state = await brain.HttpClient.GetFromJsonAsync<ProgressState>("/ui/progress/load", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal(0.4, state!.Value);
    }
}
