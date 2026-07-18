using System.Text.Json;
using System.Text.RegularExpressions;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.Services;

public class TripQueryHistorySummaryExpander(HttpClient httpClient) : ITripQueryHistorySummaryExpander
{
    private const int MaxExpandedItemsPerRequest = 8;
    private const int MaxExpandedSummaryLength = 200_000;
    private static readonly Regex _serpApiJsonEndpointRegex = new(@"https://serpapi\.com/searches/[A-Za-z0-9_-]+/[A-Za-z0-9]+\.json", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task ExpandAsync(List<TripItemResponse> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return;
        }

        var expandedItems = 0;

        foreach (var item in items.TakeWhile(_ => expandedItems < MaxExpandedItemsPerRequest))
        {
            if (item.ResultSummary is null)
            {
                continue;
            }

            var endpoint = TryExtractSerpApiJsonEndpoint(item.ResultSummary);
            if (endpoint is null)
            {
                continue;
            }

            var expandedSummary = await TryDownloadJsonSummaryAsync(endpoint, cancellationToken);
            if (string.IsNullOrWhiteSpace(expandedSummary))
            {
                continue;
            }

            item.ResultSummary = expandedSummary;
            expandedItems++;
        }
    }

    private static Uri? TryExtractSerpApiJsonEndpoint(string resultSummary)
    {
        if (string.IsNullOrWhiteSpace(resultSummary))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(resultSummary);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("truncated", out var truncatedElement) &&
                truncatedElement.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("preview", out var previewElement) &&
                previewElement.ValueKind == JsonValueKind.String)
            {
                var endpointFromPreview = TryExtractSerpApiUri(previewElement.GetString());
                if (endpointFromPreview is not null)
                {
                    return endpointFromPreview;
                }
            }
        }
        catch
        {
            // Fall through to raw text extraction.
        }

        return TryExtractSerpApiUri(resultSummary);
    }

    private static Uri? TryExtractSerpApiUri(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = _serpApiJsonEndpointRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttps &&
               string.Equals(uri.Host, "serpapi.com", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }

    private async Task<string?> TryDownloadJsonSummaryAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxExpandedSummaryLength)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxExpandedSummaryLength)
            {
                return null;
            }

            using var _ = JsonDocument.Parse(payload);
            return payload;
        }
        catch
        {
            return null;
        }
    }
}
