using System.Net;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel.Connections;
using Brain.Modules.Google;
using Google.Contracts;
using Xunit;

namespace DigitalBrain.Tests.Google;

public sealed class GoogleHttpProviderTests
{
    private static readonly ConnectionToken Token =
        new("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public void Authorization_url_is_exact_and_requests_only_gmail_readonly_and_send()
    {
        var provider = Provider(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));

        var uri = new Uri(provider.BuildAuthorizationUrl("opaque-state"));
        var query = ParseQuery(uri);

        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("accounts.google.com", uri.Host);
        Assert.True(uri.IsDefaultPort);
        Assert.Equal("/o/oauth2/v2/auth", uri.AbsolutePath);
        Assert.Empty(uri.Fragment);
        Assert.Equal(
            [
                "https://www.googleapis.com/auth/gmail.readonly",
                "https://www.googleapis.com/auth/gmail.send"
            ],
            query["scope"].Split(' '));
        Assert.Equal("opaque-state", query["state"]);
    }

    [Fact]
    public async Task Token_exchange_posts_pinned_credentials_and_parses_tokens()
    {
        string? form = null;
        var provider = Provider(new StubHandler(async request =>
        {
            form = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":120}""");
        }));

        var token = await provider.ExchangeCodeAsync("authorization-code", CancellationToken.None);

        Assert.Contains("code=authorization-code", form);
        Assert.Contains("client_id=client-id", form);
        Assert.Contains("client_secret=client-secret", form);
        Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%3A5311%2Foauth%2Fcallback%2Fgoogle", form);
        Assert.Equal("access", token.AccessToken);
        Assert.Equal("refresh", token.RefreshToken);
    }

    [Fact]
    public async Task Profile_probe_maps_success_and_unauthorized()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.Unauthorized, "{}"));
        var provider = Provider(handler);

        var healthy = await provider.ProbeAsync(Token, CancellationToken.None);
        var expired = await provider.ProbeAsync(Token, CancellationToken.None);

        Assert.Equal(ConnectionHealth.Healthy, healthy.Health);
        Assert.Equal(ConnectionHealth.TokenExpired, expired.Health);
    }

    [Fact]
    public async Task Mailbox_and_message_responses_are_parsed_into_bounded_contracts()
    {
        var body = Base64Url("hello world");
        var provider = Provider(new QueueHandler(
            Json(HttpStatusCode.OK, """
                {"messages":[{"id":"m1","threadId":"t1"}],"nextPageToken":"next"}
                """),
            Json(HttpStatusCode.OK, $$"""
                {
                  "id":"m1",
                  "threadId":"t1",
                  "internalDate":"1710000000000",
                  "payload":{
                    "headers":[
                      {"name":"From","value":"sender@example.com"},
                      {"name":"Subject","value":"Subject"}
                    ],
                    "body":{"data":"{{body}}"}
                  }
                }
                """)));

        var page = await provider.ReadMailboxAsync(Token, new GmailMailboxReadRequest(5, "page"), CancellationToken.None);
        var message = await provider.ReadMessageAsync(Token, new GmailMessageReadRequest("m1"), CancellationToken.None);

        Assert.Equal("next", page.ContinuationToken);
        Assert.Equal("m1", Assert.Single(page.Messages).MessageId);
        Assert.Equal("sender@example.com", message.SenderAddress);
        Assert.Equal("Subject", message.Subject);
        Assert.Equal("hello world", message.PlainTextBody);
    }

    [Fact]
    public async Task Send_uses_base64url_rfc2822_and_returns_provider_message_id()
    {
        string? requestJson = null;
        var provider = Provider(new StubHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """{"id":"provider-message"}""");
        }));
        var proposal = new GmailSendProposal("to@example.com", "héllo", "body", "operation-1");

        var providerMessageId = await provider.SendAsync(Token, proposal, CancellationToken.None);

        using var sent = JsonDocument.Parse(requestJson!);
        var raw = sent.RootElement.GetProperty("raw").GetString()!;
        var decoded = Encoding.UTF8.GetString(Base64UrlDecode(raw));
        Assert.Contains("To: to@example.com", decoded);
        Assert.Contains("Subject: =?UTF-8?B?", decoded);
        Assert.EndsWith("\r\n\r\nbody", decoded);
        Assert.Equal("provider-message", providerMessageId);
    }

    [Fact]
    public async Task Timeout_is_bounded_and_caller_cancellation_is_preserved()
    {
        var provider = Provider(
            new StubHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Json(HttpStatusCode.OK, "{}");
            }),
            TimeSpan.FromMilliseconds(25));

        var timeout = await Assert.ThrowsAsync<BrainException>(() =>
            provider.ExchangeCodeAsync("code", CancellationToken.None));
        Assert.Equal(BrainErrors.ProviderTimeout, timeout.Code);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.ExchangeCodeAsync("code", cancellation.Token));
    }

    [Fact]
    public async Task Non_success_and_malformed_json_fail_without_leaking_provider_body()
    {
        var provider = Provider(new QueueHandler(
            Json(HttpStatusCode.BadRequest, """{"refresh_token":"credential-from-provider"}"""),
            Json(HttpStatusCode.OK, "{not-json")));

        var providerError = await Assert.ThrowsAsync<BrainException>(() =>
            provider.ExchangeCodeAsync("code", CancellationToken.None));
        Assert.Equal(BrainErrors.ProviderError, providerError.Code);
        Assert.DoesNotContain("credential-from-provider", providerError.Message);

        var malformed = await Assert.ThrowsAsync<BrainException>(() =>
            provider.ExchangeCodeAsync("code", CancellationToken.None));
        Assert.Equal(BrainErrors.ProviderError, malformed.Code);
    }

    private static GoogleHttpProvider Provider(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(
            new StubHttpClientFactory(handler),
            new GoogleOptions(
                "client-id",
                "client-secret",
                "https://localhost:5311/oauth/callback/google",
                timeout ?? TimeSpan.FromSeconds(2)));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair[1]),
                StringComparer.Ordinal);

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _callback;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> callback)
            : this((request, _) => Task.FromResult(callback(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback)
            : this((request, _) => callback(request))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _callback(request, cancellationToken);
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }
}
