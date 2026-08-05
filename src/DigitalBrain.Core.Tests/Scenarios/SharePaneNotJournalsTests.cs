using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class SharePaneNotJournalsTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ShareGateway>()
            .AddModule<GuestPaneViewer>()
            .AddModule<OwnerPrivateAudit>();

    [Fact(DisplayName =
        "Share pane not journals: SharePaneRequested → SharedProjection redacted; guest never hears OwnerPrivateNote")]
    public async Task GuestHearsSharedProjectionNeverOwnerPrivateFacts()
    {
        var ct = Cancellation;
        var context = "ada-desk";
        var session = Brain.Session(context);
        var gatewayId = new NeuronId("sharegateway", context);
        var guestId = new NeuronId("guestpaneviewer", context);
        var auditId = new NeuronId("ownerprivateaudit", context);
        var paneId = "sales-q3";
        var secret = "Acme renewal email: CFO is angry about pricing — do not share.";
        var headline = "Q3 pipeline $4.2M (−12% MoM)";

        await session.EmitAsync(new OwnerPrivateNote(secret, headline, paneId), ct);

        var afterPrivate = await WaitForJournalAsync(
            gatewayId,
            reading => reading.AllHeard<OwnerPrivateNote>().Count == 1,
            "ShareGateway heard OwnerPrivateNote",
            ct);

        var auditReading = await WaitForJournalAsync(
            auditId,
            reading => reading.AllHeard<OwnerPrivateNote>().Count == 1,
            "owner audit heard OwnerPrivateNote",
            ct);

        var privateHeard = afterPrivate.HeardSingle<OwnerPrivateNote>();
        Assert.Equal(secret, Assert.IsType<OwnerPrivateNote>(privateHeard.Body).SecretSnippet);
        Assert.Equal(headline, Assert.IsType<OwnerPrivateNote>(privateHeard.Body).HeadlineMetric);

        // Guest has no INeuron<OwnerPrivateNote> — owner-only kind never delivered.
        var guestAfterPrivate = await ReadAsync(guestId, ct);
        Assert.Empty(guestAfterPrivate.AllHeard<OwnerPrivateNote>());
        Assert.Empty(guestAfterPrivate.Journal);

        await session.EmitAsync(new SharePaneRequested(paneId, GuestId: "beau-view-88", Recipient: "beau"), ct);

        var gatewayAfterShare = await WaitForJournalAsync(
            gatewayId,
            reading => reading.AllSaid<SharedProjection>().Count == 1
                && reading.AllHeard<SharePaneRequested>().Count == 1,
            "ShareGateway said SharedProjection",
            ct);

        var guestReading = await WaitForJournalAsync(
            guestId,
            reading => reading.AllHeard<SharedProjection>().Count == 1,
            "guest heard SharedProjection",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var privateSaid = sessionReading.SaidSingle<OwnerPrivateNote>();
        Assert.Equal("declared", privateSaid.DeliveryTo(gatewayId).Via);
        Assert.Equal("declared", privateSaid.DeliveryTo(auditId).Via);
        Assert.Null(privateSaid.DeliveryToOrNull(guestId));

        var shareSaid = sessionReading.SaidSingle<SharePaneRequested>();
        Assert.Equal("declared", shareSaid.DeliveryTo(gatewayId).Via);
        Assert.Null(shareSaid.DeliveryToOrNull(guestId));

        var projectionSaid = gatewayAfterShare.SaidSingle<SharedProjection>();
        Assert.Equal(new SynapseRef(session.Id, shareSaid.Position), projectionSaid.Cause);
        Assert.Equal("declared", projectionSaid.DeliveryTo(guestId).Via);
        var projection = Assert.IsType<SharedProjection>(projectionSaid.Body);
        Assert.Equal(paneId, projection.PaneId);
        Assert.Equal("beau-view-88", projection.GuestId);
        Assert.Equal("beau", projection.Recipient);
        Assert.Equal(headline, projection.HeadlineMetric);
        Assert.DoesNotContain(secret, projection.HeadlineMetric, StringComparison.Ordinal);
        Assert.DoesNotContain("CFO", projection.HeadlineMetric, StringComparison.Ordinal);
        Assert.DoesNotContain("email", projection.HeadlineMetric, StringComparison.OrdinalIgnoreCase);

        var guestHeard = guestReading.HeardSingle<SharedProjection>();
        Assert.Equal(gatewayId, guestHeard.Metadata.Source);
        Assert.Equal(projectionSaid.Position, guestHeard.Metadata.Sequence);
        Assert.Equal(headline, Assert.IsType<SharedProjection>(guestHeard.Body).HeadlineMetric);

        // Guest journal is projection-only — never owner-only fact kinds.
        Assert.Empty(guestReading.AllHeard<OwnerPrivateNote>());
        Assert.Empty(guestReading.AllHeard<SharePaneRequested>());
        Assert.Single(guestReading.Journal);
        Assert.All(guestReading.Journal, fact =>
        {
            Assert.IsType<SharedProjection>(fact.Body);
            Assert.IsNotType<OwnerPrivateNote>(fact.Body);
            var shared = Assert.IsType<SharedProjection>(fact.Body);
            Assert.DoesNotContain(secret, shared.HeadlineMetric, StringComparison.Ordinal);
        });

        // Owner audit still holds the private note; guest does not.
        Assert.Equal(secret, Assert.IsType<OwnerPrivateNote>(
            auditReading.HeardSingle<OwnerPrivateNote>().Body).SecretSnippet);
        Assert.Empty((await ReadAsync(guestId, ct)).AllHeard<OwnerPrivateNote>());

        // Re-read owner gateway: secret remains in owner journal only, not on SharedProjection body.
        var ownerFinal = await ReadAsync(gatewayId, ct);
        Assert.Equal(secret, Assert.IsType<OwnerPrivateNote>(
            ownerFinal.HeardSingle<OwnerPrivateNote>().Body).SecretSnippet);
        Assert.DoesNotContain(
            ownerFinal.AllSaid<SharedProjection>(),
            said => Assert.IsType<SharedProjection>(said.Body).HeadlineMetric.Contains(
                secret, StringComparison.Ordinal));
    }
}
