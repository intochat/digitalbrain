using DigitalBrain.Mocks.Tests.Support;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain.Mocks.Tests.Smoke;

public sealed class MockXSmokeTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition.ComposeMocks().RegisterNeuron<MockDashboard>("mockdashboard");

    [Fact(DisplayName = "Source publication triggers MockX and MockDashboard receives XPostObserved by declaration")]
    public async Task SourcePublicationTriggersMockXAndDashboardReceivesByDeclaration()
    {
        var ct = Cancellation;
        var sourceName = "owner-six";
        var source = new NeuronId("digitalbrain.synapse-source", sourceName);
        var mockXId = new NeuronId("mockx", sourceName);
        var dashboardId = new NeuronId("mockdashboard", sourceName);
        var createdAt = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

        await PublishAsync(
            sourceName,
            new ObserveXPost("post-1", "elonmusk", "BTC to the moon", createdAt),
            ct);

        var dashboardPage = await WaitForJournalAsync(
            dashboardId,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(XPostObserved).FullName),
            "a received X post observation",
            ct);
        var receivedByDashboard = dashboardPage.Records.Single(record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(XPostObserved).FullName);
        Assert.Equal("post-1", receivedByDashboard.Serialization.GetProperty("postId").GetString());
        Assert.Equal("elonmusk", receivedByDashboard.Serialization.GetProperty("author").GetString());
        Assert.Equal("BTC to the moon", receivedByDashboard.Serialization.GetProperty("text").GetString());
        Assert.Equal(createdAt, receivedByDashboard.Serialization.GetProperty("createdAt").GetDateTimeOffset());

        var sourcePage = await ReadAsync(source, cancellationToken: ct);
        var published = sourcePage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ObserveXPost).FullName);
        Assert.Contains(mockXId, published.DeliveryTargets);

        var mockXPage = await ReadAsync(mockXId, cancellationToken: ct);
        var receivedByMockX = mockXPage.Records.Single(record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(ObserveXPost).FullName);
        Assert.Equal(source, receivedByMockX.Origin.Source);
        Assert.Equal(published.Position, receivedByMockX.Origin.Sequence);

        var producedByMockX = mockXPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(XPostObserved).FullName);
        Assert.Contains(dashboardId, producedByMockX.DeliveryTargets);
        Assert.Equal(new SynapseReference(mockXId, receivedByMockX.Position), producedByMockX.CausedBy);

        Assert.Equal(mockXId, receivedByDashboard.Origin.Source);
        Assert.Equal(producedByMockX.Position, receivedByDashboard.Origin.Sequence);
    }
}
