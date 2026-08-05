using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class GmailWebSearchSalesforceEnrichmentTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MockGmail>()
            .AddModule<MockWebSearch>()
            .AddModule<MockSalesforce>()
            .AddModule<AccountEnricher>()
            .AddModule<EnrichmentDesk>();

    [Fact(DisplayName = "Gmail inbound -> web research -> Salesforce account enrichment")]
    public async Task EmailReceivedDrivesWebSearchThenAccountEnrichment()
    {
        var ct = Cancellation;
        var context = "sales-owner";
        var session = Brain.Session(context);
        var gmailId = new NeuronId("mockgmail", context);
        var enricherId = new NeuronId("accountenricher", context);
        var webSearchId = new NeuronId("mockwebsearch", context);
        var salesforceId = new NeuronId("mocksalesforce", context);
        var deskId = new NeuronId("enrichmentdesk", context);
        var domain = "acme-robotics.test";
        var messageId = "msg-acme-1";

        await session.EmitAsync(
            new ObserveEmail(
                messageId,
                From: $"ceo@{domain}",
                Domain: domain,
                Subject: "Intro - partnership",
                Snippet: "We build industrial arms."),
            ct);

        var deskReading = await WaitForJournalAsync(
            deskId,
            reading => reading.AllHeard<AccountEnrichmentProposed>().Count == 1
                && reading.AllHeard<AccountEnriched>().Count == 1,
            "AccountEnrichmentProposed and AccountEnriched heard on desk",
            ct);

        var proposedHeard = deskReading.HeardSingle<AccountEnrichmentProposed>();
        var proposed = Assert.IsType<AccountEnrichmentProposed>(proposedHeard.Body);
        Assert.Equal(domain, proposed.Domain);
        Assert.Null(proposed.AccountId);
        Assert.Contains(domain, proposed.FieldDiff, StringComparison.Ordinal);
        Assert.Equal(0.84, proposed.Confidence);

        var enrichedHeard = deskReading.HeardSingle<AccountEnriched>();
        var enriched = Assert.IsType<AccountEnriched>(enrichedHeard.Body);
        Assert.Equal(domain, enriched.Domain);
        Assert.Equal(proposed.FieldDiff, enriched.FieldDiff);

        var sessionReading = await ReadAsync(session.Id, ct);
        var observeSaid = sessionReading.SaidSingle<ObserveEmail>();
        Assert.Equal("declared", observeSaid.DeliveryTo(gmailId).Via);

        var gmailReading = await ReadAsync(gmailId, ct);
        var observeHeard = gmailReading.HeardSingle<ObserveEmail>();
        Assert.Equal(session.Id, observeHeard.Metadata.Source);
        Assert.Equal(observeSaid.Position, observeHeard.Metadata.Sequence);

        var emailSaid = gmailReading.SaidSingle<EmailReceived>();
        Assert.Equal("declared", emailSaid.DeliveryTo(enricherId).Via);
        Assert.Equal(new SynapseRef(session.Id, observeSaid.Position), emailSaid.Cause);
        Assert.Equal(messageId, Assert.IsType<EmailReceived>(emailSaid.Body).MessageId);

        var enricherReading = await ReadAsync(enricherId, ct);
        var emailHeard = enricherReading.HeardSingle<EmailReceived>();
        Assert.Equal(gmailId, emailHeard.Metadata.Source);
        Assert.Equal(emailSaid.Position, emailHeard.Metadata.Sequence);

        var searchAsked = enricherReading.SaidSingle<WebSearchRequested>();
        Assert.Equal("ask", searchAsked.DeliveryTo(webSearchId).Via);
        Assert.Equal(new SynapseRef(gmailId, emailSaid.Position), searchAsked.Cause);
        Assert.Equal(domain, Assert.IsType<WebSearchRequested>(searchAsked.Body).Domain);

        var webReading = await ReadAsync(webSearchId, ct);
        var searchHeard = webReading.HeardSingle<WebSearchRequested>();
        Assert.Equal(enricherId, searchHeard.Metadata.Source);
        Assert.Equal(searchAsked.Position, searchHeard.Metadata.Sequence);

        var searchCompleted = webReading.SaidSingle<WebSearchCompleted>();
        Assert.Equal(new SynapseRef(enricherId, searchAsked.Position), searchCompleted.Answers);
        Assert.NotNull(searchCompleted.DeliveryToOrNull(enricherId));
        var research = Assert.IsType<WebSearchCompleted>(searchCompleted.Body);
        Assert.Equal(domain, research.Domain);
        Assert.Contains(domain, research.Snippet, StringComparison.Ordinal);

        var researchHeard = enricherReading.HeardSingle<WebSearchCompleted>();
        Assert.Equal(webSearchId, researchHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(enricherId, searchAsked.Position), researchHeard.Answers);

        var proposeSaid = enricherReading.SaidSingle<ProposeAccountEnrichment>();
        Assert.Equal("declared", proposeSaid.DeliveryTo(salesforceId).Via);
        Assert.Equal(new SynapseRef(webSearchId, researchHeard.Metadata.Sequence), proposeSaid.Cause);

        var sfReading = await ReadAsync(salesforceId, ct);
        var proposeHeard = sfReading.HeardSingle<ProposeAccountEnrichment>();
        Assert.Equal(enricherId, proposeHeard.Metadata.Source);
        Assert.Equal(proposeSaid.Position, proposeHeard.Metadata.Sequence);

        var proposedSaid = sfReading.SaidSingle<AccountEnrichmentProposed>();
        Assert.Equal("declared", proposedSaid.DeliveryTo(deskId).Via);
        Assert.Equal(new SynapseRef(enricherId, proposeSaid.Position), proposedSaid.Cause);

        var enrichedSaid = sfReading.SaidSingle<AccountEnriched>();
        Assert.Equal("declared", enrichedSaid.DeliveryTo(deskId).Via);
        Assert.Equal(new SynapseRef(enricherId, proposeSaid.Position), enrichedSaid.Cause);

        Assert.Equal(salesforceId, proposedHeard.Metadata.Source);
        Assert.Equal(proposedSaid.Position, proposedHeard.Metadata.Sequence);
        Assert.Equal(salesforceId, enrichedHeard.Metadata.Source);
        Assert.Equal(enrichedSaid.Position, enrichedHeard.Metadata.Sequence);
    }
}
