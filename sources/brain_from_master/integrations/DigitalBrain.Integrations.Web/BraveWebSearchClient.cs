using System.Net.Http.Headers;
using System.Text.Json;
using DigitalBrain.Integrations.Web.Contracts;

namespace DigitalBrain.Integrations.Web;

internal sealed class BraveWebSearchClient(HttpClient client, string? apiKey)
{
    private const int MaximumResponseBytes = 1_048_576;
    private static readonly Uri Endpoint = new("https://api.search.brave.com/res/v1/web/search");

    public async Task<WebSearchResponse> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Web search is not configured.");
        var uri = new Uri(
            $"{Endpoint}?q={Uri.EscapeDataString(request.Query)}&count={request.MaximumResults}&safesearch=strict");
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.Add("X-Subscription-Token", apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaximumResponseBytes)
            throw new InvalidOperationException("Web search returned an oversized response.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (!document.RootElement.TryGetProperty("web", out var web) ||
            !web.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return new WebSearchResponse([]);
        var projected = new List<WebSearchResult>();
        foreach (var item in results.EnumerateArray())
        {
            if (projected.Count >= request.MaximumResults)
                break;
            if (!TryText(item, "title", 256, out var title) ||
                !TryText(item, "url", 2_048, out var url) ||
                !TryText(item, "description", 2_048, out var snippet) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                continue;
            projected.Add(new WebSearchResult(title, parsed.AbsoluteUri, snippet));
        }
        return new WebSearchResponse(projected);
    }

    private static bool TryText(
        JsonElement item,
        string name,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;
        var candidate = property.GetString()?.Trim();
        if (string.IsNullOrEmpty(candidate) ||
            candidate.Length > maximumLength ||
            candidate.Any(char.IsControl))
            return false;
        value = candidate;
        return true;
    }
}
