using DigitalBrain.Testing;

using DigitalBrain.Mocks.Tests.Support;

namespace DigitalBrain.Mocks.Tests.Smoke;

public sealed class MockXSmokeTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.ComposeMocks().AddModule<MockDashboard>();

    [Fact(DisplayName = "Session emit triggers MockX and MockDashboard hears XPostObserved by declaration")]
    public async Task SessionEmitTriggersMockXAndDashboardHearsByDeclaration()
    {
        var ct = Cancellation;
        var context = "owner-six";
        var session = Brain.Session(context);
        var mockXId = new NeuronId("mockx", context);
        var dashboardId = new NeuronId("mockdashboard", context);
        var createdAt = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

        await session.EmitAsync(
            new ObserveXPost("post-1", "elonmusk", "BTC to the moon", createdAt),
            ct);

        var observed = await WaitForAsync<XPostObserved>(dashboardId, ct);
        Assert.Equal("post-1", observed.PostId);
        Assert.Equal("elonmusk", observed.Author);
        Assert.Equal("BTC to the moon", observed.Text);
        Assert.Equal(createdAt, observed.CreatedAt);

        var sessionReading = await ReadAsync(session.Id, ct);
        var injectSaid = sessionReading.SaidSingle<ObserveXPost>();
        Assert.Equal("declared", injectSaid.DeliveryTo(mockXId).Via);

        var mockXReading = await ReadAsync(mockXId, ct);
        var injectHeard = mockXReading.HeardSingle<ObserveXPost>();
        Assert.Equal(session.Id, injectHeard.Metadata.Source);
        Assert.Equal(injectSaid.Position, injectHeard.Metadata.Sequence);

        var ambientSaid = mockXReading.SaidSingle<XPostObserved>();
        Assert.Equal("declared", ambientSaid.DeliveryTo(dashboardId).Via);
        Assert.Equal(new SynapseRef(session.Id, injectSaid.Position), ambientSaid.Cause);

        var dashboardReading = await ReadAsync(dashboardId, ct);
        var ambientHeard = dashboardReading.HeardSingle<XPostObserved>();
        Assert.Equal(mockXId, ambientHeard.Metadata.Source);
        Assert.Equal(ambientSaid.Position, ambientHeard.Metadata.Sequence);
        Assert.Equal("post-1", Assert.IsType<XPostObserved>(ambientHeard.Body).PostId);
    }
}
