using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Connections;

public static class WebResearchMeters
{
    public const string SearchRequest = "search.request";
    public const string RequestUnit = "request";
}

[GenerateSerializer, Alias("connections.web-query")]
public sealed record WebResearchQuery(string Query, int MaxResults = 5);

[GenerateSerializer, Alias("connections.web-browse")]
public sealed record BrowseWebRequest(string Url);

[GenerateSerializer, Alias("connections.web-company")]
public sealed record CompanyLookup(string Company);

[GenerateSerializer, Alias("connections.web-hit")]
public sealed record WebResearchHit(string Title, string Url, string Snippet);

[GenerateSerializer, Alias("connections.web-result")]
public sealed record WebResearchResult(IReadOnlyList<WebResearchHit> Hits, string Provider);

// The external web-research adapter. Tests substitute a deterministic provider; the product
// registers the real one and never calls a paid provider from a test.
public interface IWebResearchProvider
{
    Task<WebResearchResult> SearchAsync(WebResearchQuery query, CancellationToken cancellationToken = default);

    Task<WebResearchResult> BrowseAsync(BrowseWebRequest request, CancellationToken cancellationToken = default);

    Task<WebResearchResult> LookupCompanyAsync(CompanyLookup request, CancellationToken cancellationToken = default);
}

// Web search, browse and company lookup behave as connections: each request is metered as
// search.request through the Compute meter sink.
public interface IWebResearch
{
    ValueTask<WebResearchResult> SearchAsync(WebResearchQuery query, CallerContext caller, CancellationToken cancellationToken = default);

    ValueTask<WebResearchResult> BrowseAsync(BrowseWebRequest request, CallerContext caller, CancellationToken cancellationToken = default);

    ValueTask<WebResearchResult> LookupCompanyAsync(CompanyLookup request, CallerContext caller, CancellationToken cancellationToken = default);
}