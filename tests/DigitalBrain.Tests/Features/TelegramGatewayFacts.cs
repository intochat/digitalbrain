using System.Net;
using System.Text;
using DigitalBrain.Telegram.Aspire.Hosting;
using DigitalBrain.Telegram.Gateway;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TelegramGatewayFacts
{
    [Theory]
    [InlineData("GET", "/mcp")]
    [InlineData("POST", "/telegram/miniapp/state")]
    [InlineData("GET", "/telegram/miniapp/state/extra")]
    [InlineData("GET", "/telegram/app/../../../mcp")]
    [InlineData("GET", "/telegram/app/%2e%2e/%2e%2e/mcp")]
    [InlineData("GET", "/telegram/app/..\\..\\mcp")]
    [InlineData("GET", "/telegram/app//other.example/x")]
    [InlineData("GET", "/orleans")]
    [InlineData("POST", "/telegram/app/index.html")]
    public async Task Unknown_routes_and_path_normalization_attempts_never_reach_the_kernel(string method, string path)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var gateway = new TelegramGateway(client, "http://kernel.test:5080");
        var context = Context(method, path);
        await gateway.HandleAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Authenticated_api_proxy_has_fixed_origin_and_forwards_only_tma_authorization()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var gateway = new TelegramGateway(client, "http://kernel.test:5080");
        var context = Context("POST", "/telegram/miniapp/notifications/dismiss", "{\"eventId\":\"e\"}");
        context.Request.Host = new HostString("evil.test");
        context.Request.QueryString = new QueryString("?userId=99&url=http://evil.test");
        context.Request.Headers.Authorization = "tma signed-data";
        context.Request.Headers.Cookie = "admin=secret";
        context.Request.Headers["X-Forwarded-Host"] = "evil.test";
        context.Request.Headers["X-HTTP-Method-Override"] = "DELETE";
        context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = "not-forwarded";
        await gateway.HandleAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("http://kernel.test:5080/telegram/miniapp/notifications/dismiss", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("tma signed-data", Assert.Single(handler.Request.Headers.GetValues("Authorization")));
        Assert.Single(handler.Request.Headers);
        Assert.Equal("{\"eventId\":\"e\"}", handler.Body);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.False(context.Response.Headers.ContainsKey("WWW-Authenticate"));
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
    }

    [Theory]
    [InlineData("POST", "/telegram/webhook")]
    [InlineData("GET", "/telegram/health")]
    public async Task Provider_routes_forward_only_webhook_secret(string method, string path)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var gateway = new TelegramGateway(client, "http://kernel.test:5080");
        var context = Context(method, path, "{}");
        context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = "secret";
        context.Request.Headers.Authorization = "Basic sensitive";
        await gateway.HandleAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("secret", Assert.Single(handler.Request!.Headers.GetValues("X-Telegram-Bot-Api-Secret-Token")));
        Assert.Single(handler.Request.Headers);
    }

    [Fact]
    public async Task Static_assets_do_not_forward_credentials_and_upstream_redirects_are_refused()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Redirect);
        using var client = new HttpClient(handler);
        var gateway = new TelegramGateway(client, "http://kernel.test:5080");
        var context = Context("GET", "/telegram/app/main.dart.js");
        context.Request.Headers.Authorization = "Basic sensitive";
        context.Request.Headers.Cookie = "private=secret";
        await gateway.HandleAsync(context);
        Assert.Empty(handler.Request!.Headers);
        Assert.Equal(502, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Missing_identity_and_chunked_oversized_requests_never_reach_the_kernel()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var gateway = new TelegramGateway(client, "http://kernel.test:5080");
        var anonymous = Context("GET", "/telegram/miniapp/state");
        await gateway.HandleAsync(anonymous);
        Assert.Equal(401, anonymous.Response.StatusCode);
        var huge = Context("POST", "/telegram/webhook", new string('x', TelegramGateway.MaxBodyBytes + 1));
        huge.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = "secret";
        await gateway.HandleAsync(huge);
        Assert.Equal(413, huge.Response.StatusCode);
        Assert.Null(handler.Request);
    }

    [Fact]
    public void Tunnel_origin_uses_latest_announcement_and_never_uses_localhost_or_deceptive_hosts()
    {
        Assert.Equal("https://new-public.trycloudflare.com", TelegramTunnelLog.ReadOrigin("https://old-public.trycloudflare.com\nhttps://new-public.trycloudflare.com |"));
        Assert.Null(TelegramTunnelLog.ReadOrigin("https://api.trycloudflare.com\nhttp://localhost:5080\nhttps://safe.trycloudflare.com.evil.test"));
        Assert.Equal("https://bot.example.test", TelegramHostingExtensions.ValidatePublicOrigin("https://bot.example.test/"));
        Assert.Throws<ArgumentException>(() => TelegramHostingExtensions.ValidatePublicOrigin("https://localhost:5080"));
        Assert.Throws<ArgumentException>(() => TelegramHostingExtensions.ValidatePublicOrigin("https://bot.example.test/private"));
    }

    [Fact]
    public async Task Missing_tunnel_log_times_out_instead_of_falling_back_to_localhost()
    {
        var log = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".log");
        await Assert.ThrowsAsync<TimeoutException>(() => TelegramTunnelLog.WaitForOriginAsync(log, TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken));
    }

    private static DefaultHttpContext Context(string method, string path, string body = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        context.RequestAborted = TestContext.Current.CancellationToken;
        return context;
    }

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "private=secret");
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=private");
            response.Headers.Location = new Uri("https://evil.test");
            return response;
        }
    }
}
