using System.Net;
using System.Text.Json;
using DigitalBrain.Telegram;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TelegramConnectionFacts
{
    private const string Token = "123456:test_credential";

    [Fact]
    public async Task Registration_verifies_own_ingress_preserves_updates_and_configures_miniapp()
    {
        var status = new TelegramConnectionStatus();
        using var handler = new Provider(status.InstanceId);
        using var api = new TelegramBotApi(new HttpClient(handler));
        var options = Options();
        await new TelegramBotConnection(options, api, status, new Lifetime()).ConnectAsync(CancellationToken.None);
        Assert.Equal("Connected", status.Read().State);
        Assert.Equal("test_bot", status.Read().BotUsername);
        Assert.NotNull(status.Read().VerifiedAt);
        Assert.Equal(["getMe", "health", "setWebhook", "setChatMenuButton", "getWebhookInfo"], handler.Methods);
        var webhook = handler.Bodies["setWebhook"];
        Assert.Equal("https://bot.example/telegram/webhook", webhook.GetProperty("url").GetString());
        Assert.Equal(options.WebhookSecret, webhook.GetProperty("secret_token").GetString());
        Assert.False(webhook.GetProperty("drop_pending_updates").GetBoolean());
        Assert.Equal("message", webhook.GetProperty("allowed_updates")[0].GetString());
        Assert.Equal("https://bot.example/telegram/app/", handler.Bodies["setChatMenuButton"].GetProperty("menu_button").GetProperty("web_app").GetProperty("url").GetString());
        Assert.Equal(options.WebhookSecret, handler.HealthSecret);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(status.Read()));
    }

    [Theory]
    [InlineData("wrong-instance", true)]
    [InlineData(null, false)]
    public async Task Unready_or_different_public_instance_prevents_webhook_mutation(string? instance, bool assets)
    {
        var status = new TelegramConnectionStatus();
        using var handler = new Provider(instance ?? status.InstanceId) { Assets = assets };
        using var api = new TelegramBotApi(new HttpClient(handler));
        await new TelegramBotConnection(Options(), api, status, new Lifetime()).ConnectAsync(CancellationToken.None);
        Assert.Equal("Failed", status.Read().State);
        Assert.Equal(["getMe", "health"], handler.Methods);
    }

    [Theory]
    [InlineData("", "https://bot.example", "MissingToken")]
    [InlineData(Token, "http://bot.example", "WaitingForPublicUrl")]
    [InlineData(Token, "https://localhost", "WaitingForPublicUrl")]
    [InlineData(Token, "https://user:secret@bot.example", "WaitingForPublicUrl")]
    [InlineData(Token, "https://bot.example/path", "WaitingForPublicUrl")]
    public async Task Missing_or_unsafe_configuration_never_calls_provider(string token, string origin, string expected)
    {
        var status = new TelegramConnectionStatus();
        using var handler = new Provider(status.InstanceId);
        using var api = new TelegramBotApi(new HttpClient(handler));
        var options = Options();
        options.BotToken = token;
        options.PublicUrl = origin;
        await new TelegramBotConnection(options, api, status, new Lifetime()).ConnectAsync(CancellationToken.None);
        Assert.Equal(expected, status.Read().State);
        Assert.Empty(handler.Methods);
    }

    [Theory]
    [InlineData("getMe")]
    [InlineData("setWebhook")]
    [InlineData("setChatMenuButton")]
    [InlineData("getWebhookInfo")]
    public async Task Provider_failure_is_visible_without_leaking_token_or_response(string method)
    {
        var status = new TelegramConnectionStatus();
        using var handler = new Provider(status.InstanceId) { Failure = method };
        using var api = new TelegramBotApi(new HttpClient(handler));
        await new TelegramBotConnection(Options(), api, status, new Lifetime()).ConnectAsync(CancellationToken.None);
        Assert.Equal("Failed", status.Read().State);
        Assert.Null(status.Read().VerifiedAt);
        Assert.DoesNotContain(Token, status.Read().Error!);
        Assert.Contains("HTTP 401", status.Read().Error!);
    }

    [Fact]
    public async Task Registration_is_not_connected_when_provider_reports_another_webhook()
    {
        var status = new TelegramConnectionStatus();
        using var handler = new Provider(status.InstanceId) { Webhook = "https://another.example/telegram/webhook" };
        using var api = new TelegramBotApi(new HttpClient(handler));
        await new TelegramBotConnection(Options(), api, status, new Lifetime()).ConnectAsync(CancellationToken.None);
        Assert.Equal("Failed", status.Read().State);
        Assert.Contains("another application", status.Read().Error!);
    }

    [Theory]
    [InlineData("{\"ok\":false,\"error_code\":401,\"description\":\"123456:test_credential\"}")]
    [InlineData("123456:test_credential")]
    public async Task Sdk_provider_errors_are_sanitized(string response)
    {
        var status = new TelegramConnectionStatus();
        using var handler = new Provider(status.InstanceId) { RawResponse = response };
        using var api = new TelegramBotApi(new HttpClient(handler));
        await new TelegramBotConnection(Options(), api, status, new Lifetime()).ConnectAsync(CancellationToken.None);
        Assert.Equal("Failed", status.Read().State);
        Assert.DoesNotContain(Token, status.Read().Error!);
        Assert.Contains("getMe", status.Read().Error!);
    }

    [Theory]
    [InlineData("update_id")]
    [InlineData("is_bot")]
    public void Sdk_update_deserialization_rejects_missing_security_fields(string field)
    {
        var json = """{"update_id":123,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"text":"hello","date":1800000000}}""";
        json = field == "update_id" ? json.Replace("\"update_id\":123,", "", StringComparison.Ordinal)
            : json.Replace(",\"is_bot\":false", "", StringComparison.Ordinal);
        using var update = JsonDocument.Parse(json);
        Assert.False(TelegramWebhook.TryReadMessage(update.RootElement, "UTC", out _));
    }

    private static TelegramOptions Options() => new()
    {
        AutoConfigure = true, BotToken = Token, WebhookSecret = "webhook-secret", PublicUrl = "https://bot.example"
    };

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class Provider(string instanceId) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public Dictionary<string, JsonElement> Bodies { get; } = [];
        public string? HealthSecret { get; private set; }
        public bool Assets { get; init; } = true;
        public string? Failure { get; init; }
        public string? RawResponse { get; init; }
        public string Webhook { get; init; } = "https://bot.example/telegram/webhook";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.RequestUri!.Segments[^1];
            Methods.Add(method);
            if (request.Content is not null)
            {
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                Bodies.Add(method, body.RootElement.Clone());
            }
            if (method == Failure) { return new(HttpStatusCode.Unauthorized) { Content = new StringContent(Token) }; }
            if (RawResponse is not null) { return new(HttpStatusCode.OK) { Content = new StringContent(RawResponse) }; }
            if (method == "health")
            {
                Assert.Equal("bot.example", request.RequestUri.Host);
                HealthSecret = request.Headers.GetValues("X-Telegram-Bot-Api-Secret-Token").Single();
                return Json(new { instanceId, miniAppAvailable = Assets });
            }
            Assert.Equal("api.telegram.org", request.RequestUri.Host);
            return method switch
            {
                "getMe" => Json(new { ok = true, result = new { is_bot = true, username = "test_bot" } }),
                "getWebhookInfo" => Json(new { ok = true, result = new { url = Webhook } }),
                _ => Json(new { ok = true, result = true })
            };
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
