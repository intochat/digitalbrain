using System.Collections.ObjectModel;

namespace DigitalBrain.Integrations.Web.Contracts;

public sealed class WebSearchRequest
{
    public WebSearchRequest(string query, int maximumResults)
    {
        Query = Required(query, nameof(query), 512);
        if (maximumResults is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        MaximumResults = maximumResults;
    }

    public string Query { get; }
    public int MaximumResults { get; }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length is 0 || trimmed.Length > maximumLength || trimmed.Any(char.IsControl))
            throw new ArgumentException("Bounded canonical text is required.", parameterName);
        return trimmed;
    }
}

public sealed class WebSearchResult
{
    public WebSearchResult(string title, string url, string snippet)
    {
        Title = Required(title, nameof(title), 256);
        Url = Required(url, nameof(url), 2_048);
        Snippet = Required(snippet, nameof(snippet), 2_048);
    }

    public string Title { get; }
    public string Url { get; }
    public string Snippet { get; }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length is 0 || trimmed.Length > maximumLength || trimmed.Any(char.IsControl))
            throw new ArgumentException("Bounded canonical text is required.", parameterName);
        return trimmed;
    }
}

public sealed class WebSearchResponse
{
    public WebSearchResponse(IReadOnlyList<WebSearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count > 10)
            throw new ArgumentException("At most ten web search results are allowed.", nameof(results));
        Results = new ReadOnlyCollection<WebSearchResult>(results.ToArray());
    }

    public IReadOnlyList<WebSearchResult> Results { get; }
}

public interface IWebSearchReader
{
    Task<WebSearchResponse> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken = default);
}
