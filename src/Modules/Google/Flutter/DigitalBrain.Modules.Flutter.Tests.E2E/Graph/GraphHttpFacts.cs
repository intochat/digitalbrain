using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Graph;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Graph;

public sealed class GraphHttpFacts
{
    [Fact]
    public async Task GetMatchesRender()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        await brain.Get<IGraph>(UiScope.Key("workspace-a", "net")).Render("N", [new GraphNode("a", "A")], []);
        var state = await brain.HttpClient.GetFromJsonAsync<GraphState>("/workspaces/workspace-a/ui/graphs/net", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        Assert.Single(state!.Nodes);
    }
}