using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Kernel;
using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class BrainGraphHttpTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Http_subscription_changes_the_real_source_owned_edge_and_removes_it_completely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = fixture.CreateHttpClient("kernel");
        var chatName = $"graph-{Guid.NewGuid():N}";
        var path = $"/chats/{chatName}/brain";
        var initial = await http.GetFromJsonAsync<BrainGraphSnapshot>(path, cancellationToken);
        Assert.NotNull(initial);
        Assert.Contains(initial.Nodes, node => node.Id == "assistant:assistant");
        var request = new BrainGraphSubscriptionRequest("assistant:assistant", initial.RootId, "Note", true);

        using var subscribed = await http.PostAsJsonAsync(path + "/subscriptions", request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, subscribed.StatusCode);
        try
        {
            var bound = await http.GetFromJsonAsync<BrainGraphSnapshot>(path, cancellationToken);
            Assert.NotNull(bound);
            var edge = Assert.Single(bound.Synapses, edge => edge.SourceId == request.SourceId
                && edge.TargetId == request.TargetId && edge.SignalType == request.SignalType);
            Assert.Equal("Bound", edge.Kind);
            Assert.True(edge.CanUnsubscribe);
        }
        finally
        {
            using var removed = await http.PostAsJsonAsync(path + "/subscriptions",
                request with { Subscribed = false }, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        }

        var after = await http.GetFromJsonAsync<BrainGraphSnapshot>(path, cancellationToken);
        Assert.NotNull(after);
        Assert.DoesNotContain(after.Synapses, edge => edge.SourceId == request.SourceId
            && edge.TargetId == request.TargetId && edge.SignalType == request.SignalType);
        Assert.Contains(after.Activity, item => item.SignalType == "Unsubscribe");

        var foreign = request with
        {
            TargetId = "chat:" + PrincipalPartition.InstanceName(PrincipalId.New(), chatName),
        };
        using var refused = await http.PostAsJsonAsync(path + "/subscriptions", foreign, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
