using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Color;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Color;

public sealed class ColorHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IColor>("accent").Set("#0a84ff");
        var state = await brain.HttpClient.GetFromJsonAsync<ColorState>("/ui/colors/accent", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("#0A84FF", state!.Hex);
    }
}
