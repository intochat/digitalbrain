using DigitalBrain.Contracts.Enforcement;

namespace IntoChat.Apps;

internal static class WebResearchMeters
{
    public const string SearchRequest = "search.request";
    public const string RequestUnit = "request";
}

[GenerateSerializer, Alias("connections.web-query")]
internal sealed record WebResearchQuery(string Query, int MaxResults = 5);

[GenerateSerializer, Alias("connections.web-browse")]
internal sealed record BrowseWebRequest(string Url);

[GenerateSerializer, Alias("connections.web-company")]
internal sealed record CompanyLookup(string Company);

[GenerateSerializer, Alias("connections.web-hit")]
internal sealed record WebResearchHit(string Title, string Url, string Snippet);

[GenerateSerializer, Alias("connections.web-result")]
internal sealed record WebResearchResult(IReadOnlyList<WebResearchHit> Hits, string Provider);

// Lead Generator's replaceable research adapter. The default provider returns deterministic data.
internal interface IWebResearchProvider
{
    Task<WebResearchResult> SearchAsync(WebResearchQuery query, CancellationToken cancellationToken = default);

    Task<WebResearchResult> BrowseAsync(BrowseWebRequest request, CancellationToken cancellationToken = default);

    Task<WebResearchResult> LookupCompanyAsync(CompanyLookup request, CancellationToken cancellationToken = default);
}

// Each research request is metered through the Compute meter sink.
internal interface IWebResearch
{
    ValueTask<WebResearchResult> SearchAsync(WebResearchQuery query, CallerContext caller, CancellationToken cancellationToken = default);

    ValueTask<WebResearchResult> BrowseAsync(BrowseWebRequest request, CallerContext caller, CancellationToken cancellationToken = default);

    ValueTask<WebResearchResult> LookupCompanyAsync(CompanyLookup request, CallerContext caller, CancellationToken cancellationToken = default);
}
