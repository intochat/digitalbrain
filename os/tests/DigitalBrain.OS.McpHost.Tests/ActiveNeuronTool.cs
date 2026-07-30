using System.Text.Json;
using DigitalBrain.Chat;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.McpHost.Tests;

public sealed class ActiveNeuronTool(OSMcpFixture fixture)
{
    [Fact(DisplayName = "list_active_neurons is owner scoped and exposes no silo address")]
    public async Task ListActiveNeuronsIsOwnerScopedAndRedacted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var other = test.Owner("other");

        await test.Client.GetGrainProxy<IChat>("mine").Read();
        await other.Client.GetGrainProxy<IChat>("theirs").Read();

        var tools = new DigitalBrainIntrospectionTools(test.Client, test.Cluster.Client);
        var neurons = await tools.ListActiveNeuronsAsync();
        var json = JsonSerializer.Serialize(neurons);

        Assert.Contains(
            neurons,
            neuron => neuron.GrainType == "chat"
                && neuron.Identity == $"{test.Client.Owner.Value}/mine");
        Assert.DoesNotContain(
            neurons,
            neuron => neuron.Identity.StartsWith(
                $"{other.Id.Value}/",
                StringComparison.Ordinal));
        Assert.DoesNotContain("\"silo\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
