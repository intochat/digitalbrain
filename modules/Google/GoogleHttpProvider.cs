using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel.Connections;
using Google.Contracts;

namespace Brain.Modules.Google;

public sealed class GoogleHttpProvider(
    IHttpClientFactory httpClientFactory,
    GoogleOptions options) : IConnectionProvider, IGmailProvider
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ProfileEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/profile";
    private const string MessagesEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages";
    private const string SendEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";
    private const string GmailReadonlyScope = "https://www.googleapis.com/auth/gmail.readonly";
    private const string GmailSendScope = "https://www.googleapis.com/auth/gmail.send";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string BuildAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["scope"] = $"{GmailReadonlyScope} {GmailSendScope}",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        return AuthorizationEndpoint + "?" + BuildQueryString(query);
    }

    public async Task<ConnectionToken> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["redirect_uri"] = options.RedirectUri
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        var responseJson = await SendForBodyAsync(request, ct);
        try
        {
            using var response = JsonDocument.Parse(responseJson);
            var root = response.RootElement;
            var accessToken = RequiredString(root, "access_token");
            var refreshToken = root.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString() ?? string.Empty
                : string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var seconds)
                ? seconds
                : 3600;
            return new ConnectionToken(
                accessToken,
                refreshToken,
                DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw ProviderPayloadError();
        }
    }

    public async Task<ProbeResult> ProbeAsync(ConnectionToken token, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, ProfileEndpoint, token);
        try
        {
            using var response = await SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new ProbeResult(ConnectionHealth.TokenExpired, "Google profile authorization expired.");
            if (!response.IsSuccessStatusCode)
                return new ProbeResult(ConnectionHealth.ProviderError, $"Google profile probe returned {(int)response.StatusCode}.");
            return new ProbeResult(ConnectionHealth.Healthy, "Google profile probe succeeded.");
        }
        catch (BrainException exception) when (exception.Code == BrainErrors.ProviderTimeout)
        {
            return new ProbeResult(ConnectionHealth.NetworkError, "Google profile probe timed out.");
        }
        catch (BrainException)
        {
            return new ProbeResult(ConnectionHealth.NetworkError, "Google profile probe failed.");
        }
    }

    public async Task<GmailMailboxPage> ReadMailboxAsync(
        ConnectionToken token,
        GmailMailboxReadRequest request,
        CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["maxResults"] = request.Limit.ToString(CultureInfo.InvariantCulture)
        };
        if (request.ContinuationToken is { } continuationToken)
            query["pageToken"] = continuationToken;

        using var message = Authorized(
            HttpMethod.Get,
            $"{MessagesEndpoint}?{BuildQueryString(query)}",
            token);
        var responseJson = await SendForBodyAsync(message, ct);
        try
        {
            using var response = JsonDocument.Parse(responseJson);
            var root = response.RootElement;
            var messages = root.TryGetProperty("messages", out var entries)
                ? entries.EnumerateArray()
                    .Select(entry => new GmailMessageSummary(
                        RequiredString(entry, "id"),
                        OptionalString(entry, "threadId"),
                        DateTimeOffset.UnixEpoch,
                        null,
                        null))
                    .ToArray()
                : [];
            return new GmailMailboxPage(messages, OptionalString(root, "nextPageToken"));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw ProviderPayloadError();
        }
    }

    public async Task<GmailMessage> ReadMessageAsync(
        ConnectionToken token,
        GmailMessageReadRequest request,
        CancellationToken ct)
    {
        using var message = Authorized(
            HttpMethod.Get,
            $"{MessagesEndpoint}/{Uri.EscapeDataString(request.MessageId)}?format=full",
            token);
        var responseJson = await SendForBodyAsync(message, ct);
        try
        {
            using var response = JsonDocument.Parse(responseJson);
            var root = response.RootElement;
            var payload = root.GetProperty("payload");
            var headers = payload.TryGetProperty("headers", out var headerEntries)
                ? headerEntries.EnumerateArray()
                    .Where(header =>
                        header.TryGetProperty("name", out var name) &&
                        header.TryGetProperty("value", out _))
                    .ToDictionary(
                        header => header.GetProperty("name").GetString() ?? string.Empty,
                        header => header.GetProperty("value").GetString(),
                        StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var receivedAt = root.TryGetProperty("internalDate", out var internalDate) &&
                long.TryParse(internalDate.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                    : DateTimeOffset.UnixEpoch;

            return new GmailMessage(
                RequiredString(root, "id"),
                OptionalString(root, "threadId"),
                receivedAt,
                headers.GetValueOrDefault("From"),
                headers.GetValueOrDefault("Subject"),
                ReadPlainText(payload));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            throw ProviderPayloadError();
        }
    }

    public async Task<string> ListAsync(ConnectionToken token, int max, CancellationToken ct)
    {
        var page = await ReadMailboxAsync(token, new GmailMailboxReadRequest(max), ct);
        return JsonSerializer.Serialize(new
        {
            messages = page.Messages.Select(message => new
            {
                id = message.MessageId,
                threadId = message.ThreadId
            }),
            nextPageToken = page.ContinuationToken
        }, JsonOptions);
    }

    public Task<string> SendAsync(ConnectionToken token, string payloadJson, CancellationToken ct)
    {
        try
        {
            using var payload = JsonDocument.Parse(payloadJson);
            var proposal = new GmailSendProposal(
                RequiredString(payload.RootElement, "to"),
                RequiredString(payload.RootElement, "subject"),
                RequiredString(payload.RootElement, "body"),
                "legacy");
            return SendAsync(token, proposal, ct);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw ProviderPayloadError();
        }
    }

    public async Task<string> SendAsync(
        ConnectionToken token,
        GmailSendProposal proposal,
        CancellationToken ct)
    {
        var raw = Base64UrlEncode(BuildRfc2822Message(
            proposal.Recipient,
            proposal.Subject,
            proposal.Body));
        using var request = Authorized(HttpMethod.Post, SendEndpoint, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { raw }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        var responseJson = await SendForBodyAsync(request, ct);
        try
        {
            using var response = JsonDocument.Parse(responseJson);
            return RequiredString(response.RootElement, "id");
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw ProviderPayloadError();
        }
    }

    private async Task<string> SendForBodyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new BrainException(
                BrainErrors.ProviderError,
                $"Google provider returned HTTP {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            using var client = httpClientFactory.CreateClient();
            return await client.SendAsync(request, linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new BrainException(
                BrainErrors.ProviderTimeout,
                $"Google provider timed out after {options.RequestTimeout.TotalSeconds:g} seconds.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new BrainException(BrainErrors.ProviderError, "Google provider request failed.");
        }
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string uri,
        ConnectionToken token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return request;
    }

    private static string ReadPlainText(JsonElement payload)
    {
        if (payload.TryGetProperty("mimeType", out var mimeType) &&
            string.Equals(mimeType.GetString(), "text/plain", StringComparison.OrdinalIgnoreCase) &&
            TryReadBody(payload, out var direct))
            return direct;
        if (TryReadBody(payload, out direct) && !string.IsNullOrEmpty(direct))
            return direct;
        if (!payload.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var part in parts.EnumerateArray())
        {
            var text = ReadPlainText(part);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return string.Empty;
    }

    private static bool TryReadBody(JsonElement payload, out string body)
    {
        body = string.Empty;
        if (!payload.TryGetProperty("body", out var bodyElement) ||
            !bodyElement.TryGetProperty("data", out var dataElement) ||
            dataElement.GetString() is not { Length: > 0 } encoded)
            return false;

        var padded = encoded.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        body = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        return true;
    }

    private static string BuildRfc2822Message(string to, string subject, string body)
    {
        var encodedSubject = Convert.ToBase64String(Encoding.UTF8.GetBytes(subject));
        return string.Join("\r\n",
            $"To: {to}",
            $"Subject: =?UTF-8?B?{encodedSubject}?=",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            body);
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Google response omitted {property}.");

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static BrainException ProviderPayloadError() =>
        new(BrainErrors.ProviderError, "Google provider returned an invalid response.");

    private static string Base64UrlEncode(string message) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(message))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string BuildQueryString(IReadOnlyDictionary<string, string> values) =>
        string.Join(
            "&",
            values.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
}
