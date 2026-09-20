using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tree;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TreeHttpFacts
{
    [Fact]
    public async Task GetMatchesSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<FlutterModule>(flutter => flutter.WithoutHost())
            .StartAsync(ct);
        await brain.Get<ITree>("fs").Set([new TreeNode("root", null, "root")]);
        var state = await brain.HttpClient.GetFromJsonAsync<TreeState>("/ui/trees/fs", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Equal("root", state!.SelectedId);
    }
}
