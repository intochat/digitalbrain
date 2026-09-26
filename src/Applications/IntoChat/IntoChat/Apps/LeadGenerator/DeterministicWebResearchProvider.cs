namespace IntoChat.Apps;

// The offline stand-in that keeps the product profile free of paid web calls. A hosted adapter
// replaces it through the same IWebResearchProvider seam.
internal sealed class DeterministicWebResearchProvider : IWebResearchProvider
{
    public Task<WebResearchResult> SearchAsync(WebResearchQuery query, CancellationToken cancellationToken = default)
        => Result("search", query.Query, "https://search.example.test");

    public Task<WebResearchResult> BrowseAsync(BrowseWebRequest request, CancellationToken cancellationToken = default)
        => Result("browse", request.Url, request.Url);

    public Task<WebResearchResult> LookupCompanyAsync(CompanyLookup request, CancellationToken cancellationToken = default)
        => Result("company", request.Company, "https://companies.example.test/" + Uri.EscapeDataString(request.Company));

    private static Task<WebResearchResult> Result(string kind, string subject, string url)
        => Task.FromResult(new WebResearchResult(
            [new WebResearchHit($"{kind}: {subject}", url, "deterministic result")],
            "deterministic"));
}
