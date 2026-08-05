using System.Collections.Immutable;
using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record ResearchBriefUserAsked(
    string Topic,
    ImmutableArray<string> Entities) : Synapse;

public sealed record ResearchBriefRequested(
    string BriefId,
    string Topic,
    ImmutableArray<string> Entities) : Synapse;

public sealed record ResearchClaim(
    string Text,
    ImmutableArray<string> SupportUrls,
    double Confidence);

public sealed record ResearchClaimsProposed(
    string BriefId,
    ImmutableArray<ResearchClaim> Claims) : Synapse;

public sealed record UnsupportedClaimDropped(
    string BriefId,
    string Text,
    string Reason) : Synapse;

public sealed record ResearchCitationTable(
    string BriefId,
    ImmutableArray<string> Urls,
    ImmutableArray<string> Titles) : Synapse;

public sealed record ResearchBriefArtifact(
    string BriefId,
    string Markdown,
    int CitationCount) : Synapse;

// Research desk: multi WebSearch ask; claims only cite journaled search URLs; drop unsupported.
public sealed class ResearchBriefDesk : Neuron<ResearchBriefState>,
    INeuron<ResearchBriefUserAsked>,
    INeuron<WebSearchCompleted>
{
    public Task HandleAsync(ResearchBriefUserAsked fact, CancellationToken cancellationToken)
    {
        var briefId = $"brief-{fact.Topic.GetHashCode(StringComparison.Ordinal):x6}";
        State.BriefId = briefId;
        State.Topic = fact.Topic;
        State.PendingSearches = fact.Entities.Length;
        State.CompletedSearches = 0;
        State.Sources.Clear();
        State.Titles.Clear();

        Emit(new ResearchBriefRequested(briefId, fact.Topic, fact.Entities));
        foreach (var entity in fact.Entities)
        {
            Ask<WebSearchCompleted>(new WebSearchRequested(
                Query: $"{fact.Topic} {entity}",
                Domain: entity));
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(WebSearchCompleted fact, CancellationToken cancellationToken)
    {
        if (State.BriefId is null || State.PendingSearches == 0)
        {
            return Task.CompletedTask;
        }

        State.Sources.Add(fact.Source);
        State.Titles.Add($"{fact.Domain}: {fact.Snippet}");
        State.CompletedSearches++;

        if (State.CompletedSearches < State.PendingSearches)
        {
            return Task.CompletedTask;
        }

        AssembleBrief();
        return Task.CompletedTask;
    }

    private void AssembleBrief()
    {
        var briefId = State.BriefId!;
        var allowed = State.Sources.ToImmutableArray();
        var groundedClaims = new List<ResearchClaim>();

        // Grounded claims: every support URL must appear in journaled WebSearchCompleted sources.
        foreach (var (source, index) in allowed.Select((s, i) => (s, i)))
        {
            groundedClaims.Add(new ResearchClaim(
                Text: $"Claim about {State.Topic} from source {index + 1}",
                SupportUrls: [source],
                Confidence: 0.8));
        }

        // Adversarial invented URL — must be dropped, never silent fill.
        const string invented = "https://hallucinated.example/fake";
        var inventedSupported = allowed.Contains(invented, StringComparer.Ordinal);
        if (!inventedSupported)
        {
            Emit(new UnsupportedClaimDropped(
                briefId,
                Text: "Invented market share 99%",
                Reason: "support-url-not-in-search-answers"));
        }

        Emit(new ResearchClaimsProposed(briefId, [.. groundedClaims]));
        Emit(new ResearchCitationTable(
            briefId,
            Urls: allowed,
            Titles: [.. State.Titles]));
        Emit(new ResearchBriefArtifact(
            briefId,
            Markdown: $"# Brief: {State.Topic}\n\n{groundedClaims.Count} cited claims.",
            CitationCount: allowed.Length));
    }
}

public sealed class ResearchBriefState
{
    public string? BriefId { get; set; }
    public string? Topic { get; set; }
    public int PendingSearches { get; set; }
    public int CompletedSearches { get; set; }
#pragma warning disable CA1002, CA2227
    public List<string> Sources { get; set; } = [];
    public List<string> Titles { get; set; } = [];
#pragma warning restore CA1002, CA2227
}

// Catalog sinks for research ambient artifacts.
public sealed class ResearchBriefShellLedger : Neuron,
    INeuron<ResearchBriefRequested>,
    INeuron<ResearchClaimsProposed>,
    INeuron<UnsupportedClaimDropped>,
    INeuron<ResearchCitationTable>,
    INeuron<ResearchBriefArtifact>
{
    public Task HandleAsync(ResearchBriefRequested fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ResearchClaimsProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(UnsupportedClaimDropped fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ResearchCitationTable fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ResearchBriefArtifact fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
