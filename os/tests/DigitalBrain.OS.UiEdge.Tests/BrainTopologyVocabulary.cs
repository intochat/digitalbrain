using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Introspection;
using DigitalBrain.Shell;
using Xunit;

namespace DigitalBrain.OS.UiEdge.Tests;

public sealed class BrainTopologyVocabulary(UiEdgeFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName =
        "/brain/topology answers from the introspection neuron, so the neuron that computed it is itself activated")]
    public async Task TopologyIsAnsweredByTheIntrospectionNeuron()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var served = await http.GetFromJsonAsync<BrainTopologySnapshot>(
            UiEdgeContract.BrainTopologyPath,
            Json,
            cancellationToken);

        Assert.NotNull(served);
        Assert.Contains(served.Neurons, neuron => neuron.GrainType == "introspection");
        Assert.Contains(served.Modules, module => module.Id == ShellModule.Id.Value);
        Assert.Contains(served.Modules, module => module.Id == ChatModule.Id.Value);
        Assert.Contains(served.Modules, module => module.Id == IntrospectionModule.Id.Value);
        Assert.All(
            served.Neurons,
            neuron => Assert.StartsWith("cluster-", neuron.Placement, StringComparison.Ordinal));
    }

    // A live stall would mean holding an HTTP request open for the whole bound; pinning the
    // relationship the bound exists to establish is the proportionate proof.
    [Fact(DisplayName =
        "the topology route gives up well before the grain-call response timeout it exists to tighten")]
    public void TheTopologyWaitIsBounded()
    {
        Assert.True(BrainEndpoints.TopologyReplyBound > TimeSpan.Zero);
        Assert.True(
            BrainEndpoints.TopologyReplyBound
                < TimeSpan.Parse(NeuronCallTimeouts.LongRunning, CultureInfo.InvariantCulture),
            $"A topology bound of {BrainEndpoints.TopologyReplyBound} gives up no sooner than the "
            + $"{NeuronCallTimeouts.LongRunning} response timeout it exists to tighten.");
    }

    [Fact(DisplayName = "the UI edge assembly holds no topology reader of its own")]
    public void TheEdgeHoldsNoSecondTopologyComputation()
        => Assert.DoesNotContain(
            typeof(UiEdgeContract).Assembly.GetTypes(),
            type => type.Name.Contains("TopologyReader", StringComparison.Ordinal));
}
