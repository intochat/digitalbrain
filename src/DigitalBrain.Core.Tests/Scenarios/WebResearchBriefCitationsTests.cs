using System.Collections.Immutable;
using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class WebResearchBriefCitationsTests(BrainTestClusters clusters)
    : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ResearchBriefDesk>()
            .AddModule<MockWebSearch>()
            .AddModule<ResearchBriefShellLedger>();

    [Fact(DisplayName =
        "Web research brief: multi WebSearch → claims cite only journaled URLs; UnsupportedClaimDropped for invented URL; citation table")]
    public async Task ClaimsGroundedInJournaledSearchAnswers()
    {
        var ct = Cancellation;
        var context = "research-contoso";
        var session = Brain.Session(context);
        var deskId = new NeuronId("researchbriefdesk", context);
        var searchId = new NeuronId("mockwebsearch", context);
        var shellId = new NeuronId("researchbriefshellledger", context);
        var entities = ImmutableArray.Create("contoso.test", "fabrikam.test");

        await session.EmitAsync(
            new ResearchBriefUserAsked("market position Contoso vs Fabrikam", entities),
            ct);

        var desk = await WaitForJournalAsync(
            deskId,
            reading => reading.AllSaid<ResearchBriefRequested>().Count == 1
                && reading.AllSaid<WebSearchRequested>().Count == 2
                && reading.AllHeard<WebSearchCompleted>().Count == 2
                && reading.AllSaid<ResearchClaimsProposed>().Count == 1
                && reading.AllSaid<UnsupportedClaimDropped>().Count == 1
                && reading.AllSaid<ResearchCitationTable>().Count == 1
                && reading.AllSaid<ResearchBriefArtifact>().Count == 1,
            "desk completed multi-search brief with citations",
            ct);

        var shell = await WaitForJournalAsync(
            shellId,
            reading => reading.AllHeard<ResearchClaimsProposed>().Count == 1
                && reading.AllHeard<UnsupportedClaimDropped>().Count == 1
                && reading.AllHeard<ResearchCitationTable>().Count == 1
                && reading.AllHeard<ResearchBriefArtifact>().Count == 1,
            "shell heard claims + drop + citations + artifact",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var userSaid = sessionReading.SaidSingle<ResearchBriefUserAsked>();
        Assert.Equal("declared", userSaid.DeliveryTo(deskId).Via);

        var asks = desk.AllSaid<WebSearchRequested>();
        Assert.Equal(2, asks.Count);
        Assert.All(asks, said =>
        {
            Assert.Equal("ask", said.DeliveryTo(searchId).Via);
            Assert.Equal(new SynapseRef(session.Id, userSaid.Position), said.Cause);
        });

        var searchReading = await ReadAsync(searchId, ct);
        var answers = searchReading.AllSaid<WebSearchCompleted>();
        Assert.Equal(2, answers.Count);
        var journaledUrls = answers
            .Select(said => Assert.IsType<WebSearchCompleted>(said.Body).Source)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2, journaledUrls.Count);

        var claims = Assert.IsType<ResearchClaimsProposed>(
            desk.SaidSingle<ResearchClaimsProposed>().Body);
        Assert.Equal(2, claims.Claims.Length);
        Assert.All(claims.Claims, claim =>
        {
            Assert.NotEmpty(claim.SupportUrls);
            Assert.All(claim.SupportUrls, url => Assert.Contains(url, journaledUrls));
        });

        var dropped = Assert.IsType<UnsupportedClaimDropped>(
            desk.SaidSingle<UnsupportedClaimDropped>().Body);
        Assert.Contains("not-in-search", dropped.Reason, StringComparison.Ordinal);

        var table = Assert.IsType<ResearchCitationTable>(
            desk.SaidSingle<ResearchCitationTable>().Body);
        Assert.Equal(2, table.Urls.Length);
        Assert.All(table.Urls, url => Assert.Contains(url, journaledUrls));
        Assert.DoesNotContain(
            table.Urls,
            url => url.Contains("hallucinated", StringComparison.Ordinal));

        var artifact = Assert.IsType<ResearchBriefArtifact>(
            desk.SaidSingle<ResearchBriefArtifact>().Body);
        Assert.Equal(2, artifact.CitationCount);
        Assert.Contains("Brief", artifact.Markdown, StringComparison.Ordinal);

        Assert.Equal(deskId, shell.HeardSingle<ResearchCitationTable>().Metadata.Source);
    }
}
