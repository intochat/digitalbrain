using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigitalBrain.Google.Gmail;

internal sealed class GmailMailboxClient : IGmailMailbox, IDisposable
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://gmail.googleapis.com/"),
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 1_048_576,
    };

    public async Task<GmailWatchReceipt> WatchAsync(string accessToken, string topicName, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, "gmail/v1/users/me/watch", accessToken);
        request.Content = JsonContent.Create(new { topicName, labelIds = new[] { "INBOX" }, labelFilterBehavior = "INCLUDE" });
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        using var json = await ReadAsync(response, cancellationToken).ConfigureAwait(false);
        var historyId = json.RootElement.GetProperty("historyId").GetString();
        var expiration = json.RootElement.GetProperty("expiration").GetString();
        if (string.IsNullOrWhiteSpace(historyId) || !long.TryParse(expiration, out var unixMilliseconds))
        {
            throw new GmailUnavailableException("Gmail returned an invalid watch response.");
        }

        return new GmailWatchReceipt(historyId, DateTimeOffset.UnixEpoch.AddMilliseconds(unixMilliseconds));
    }

    public async Task<IReadOnlyList<GmailIncoming>> ListAddedAsync(string accessToken, string startHistoryId, CancellationToken cancellationToken)
    {
        using var historyRequest = Authorized(HttpMethod.Get, "gmail/v1/users/me/history?startHistoryId=" + Uri.EscapeDataString(startHistoryId) + "&historyTypes=messageAdded", accessToken);
        using var historyResponse = await _http.SendAsync(historyRequest, cancellationToken).ConfigureAwait(false);
        if (historyResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        using var history = await ReadAsync(historyResponse, cancellationToken).ConfigureAwait(false);
        var ids = new List<string>();
        if (history.RootElement.TryGetProperty("history", out var records))
        {
            foreach (var record in records.EnumerateArray())
            {
                if (!record.TryGetProperty("messagesAdded", out var added)) { continue; }
                foreach (var item in added.EnumerateArray())
                {
                    var id = item.GetProperty("message").GetProperty("id").GetString();
                    if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id, StringComparer.Ordinal)) { ids.Add(id); }
                    if (ids.Count == 20) { break; }
                }
                if (ids.Count == 20) { break; }
            }
        }

        var messages = new List<GmailIncoming>(ids.Count);
        foreach (var id in ids)
        {
            using var messageRequest = Authorized(HttpMethod.Get, "gmail/v1/users/me/messages/" + Uri.EscapeDataString(id) + "?format=metadata&metadataHeaders=Subject", accessToken);
            using var messageResponse = await _http.SendAsync(messageRequest, cancellationToken).ConfigureAwait(false);
            using var message = await ReadAsync(messageResponse, cancellationToken).ConfigureAwait(false);
            messages.Add(new GmailIncoming(id, Subject(message.RootElement)));
        }

        return messages;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken)
    {
        GmailTokenRefresh.ValidateToken(accessToken);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new GmailUnreachableException($"Gmail returned HTTP {(int)response.StatusCode}.");
        }

        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new GmailUnavailableException("Gmail returned an invalid response shape."); }
    }

    private static string Subject(JsonElement message)
    {
        if (!message.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("headers", out var headers))
        {
            return "";
        }

        foreach (var header in headers.EnumerateArray())
        {
            if (string.Equals(header.GetProperty("name").GetString(), "Subject", StringComparison.OrdinalIgnoreCase))
            {
                return header.GetProperty("value").GetString() ?? "";
            }
        }

        return "";
    }

    public void Dispose() => _http.Dispose();
}
