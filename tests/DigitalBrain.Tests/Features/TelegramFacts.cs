using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Telegram;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using DigitalBrain.UI;
using Microsoft.Extensions.Time.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TelegramFacts
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);

    [Fact]
    public void Signed_identity_is_verified_and_hash_covers_signature_field()
    {
        var signed = SignedData(Now.ToUnixTimeSeconds());
        Assert.True(TelegramAuthentication.TryValidateInitData("tma " + signed, "test-token", Now, out var id));
        Assert.Equal(42, id);
        Assert.False(TelegramAuthentication.TryValidateInitData("tma " + signed.Replace("signature=extra", "signature=forged", StringComparison.Ordinal), "test-token", Now, out _));
        Assert.False(TelegramAuthentication.TryValidateInitData("tma " + signed, "other-token", Now, out _));
        Assert.False(TelegramAuthentication.TryValidateInitData("tma " + signed + "&user=%7B%22id%22%3A99%7D", "test-token", Now, out _));
    }

    [Theory]
    [InlineData(-3601)]
    [InlineData(31)]
    public void Stale_and_future_init_data_are_rejected(long offset)
    {
        Assert.False(TelegramAuthentication.TryValidateInitData("tma " + SignedData(Now.ToUnixTimeSeconds() + offset), "test-token", Now, out _));
    }

    [Theory]
    [InlineData("tma user=%ZZ")]
    [InlineData("tma hash=none")]
    [InlineData("Bearer anything")]
    public void Malformed_authorization_fails_closed(string value) =>
        Assert.False(TelegramAuthentication.TryValidateInitData(value, "test-token", Now, out _));

    [Fact]
    public void Webhook_only_accepts_private_human_sender_matching_chat()
    {
        Assert.True(TelegramAuthentication.IsWebhookSecretValid("secret", "secret"));
        Assert.False(TelegramAuthentication.IsWebhookSecretValid("secret", "wrong"));
        Assert.False(TelegramAuthentication.IsWebhookSecretValid("", ""));
        using var valid = JsonDocument.Parse(Update());
        Assert.True(TelegramWebhook.TryReadMessage(valid.RootElement, "UTC", out var message));
        Assert.Equal("123", message!.EventId);
        Assert.Equal(42, message.UserId);
        foreach (var body in new[] { Update(chat: 99), Update(type: "group"), Update(bot: true), Update().Replace("\"update_id\":123", "\"update_id\":\"123\"", StringComparison.Ordinal) })
        {
            using var invalid = JsonDocument.Parse(body);
            Assert.False(TelegramWebhook.TryReadMessage(invalid.RootElement, "UTC", out _));
        }
    }

    [Fact]
    public async Task Signed_miniapp_requests_can_only_read_and_mutate_the_verified_users_inbox()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TelegramModule), typeof(UIModule), typeof(TimeModule)]) });
        var own = brain.Grains.GetGrain<INotification>(new NeuronId("notification", "telegram-42").ToGrainId());
        var other = brain.Grains.GetGrain<INotification>(new NeuronId("notification", "telegram-99").ToGrainId());
        await own.Publish(new(CommandId.New(), "same-event", "Own", "private42", "incoming"), TestContext.Current.CancellationToken);
        await other.Publish(new(CommandId.New(), "same-event", "Other", "private99", "incoming"), TestContext.Current.CancellationToken);
        await ReactionWait.UntilAsync(async () => await own.ReadPendingCount() == 0 && await other.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        using var services = new ServiceCollection().AddSingleton<IGrainFactory>(brain.Grains)
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Now)).BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        new TelegramHttpSurface(new TelegramOptions { BotToken = "test-token", WebhookSecret = "secret", MiniAppRoot = "" }).Map(app);
        app.Run(context => { context.Response.StatusCode = 418; return Task.CompletedTask; });
        var pipeline = app.Build();
        var read = Request("GET", "/telegram/miniapp/state", "");
        read.Request.QueryString = new QueryString("?userId=99&neuron=notification:telegram-99");
        await pipeline(read);
        Assert.Equal(200, read.Response.StatusCode);
        var json = Encoding.UTF8.GetString(((MemoryStream)read.Response.Body).ToArray());
        Assert.Contains("private42", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private99", json, StringComparison.Ordinal);
        var injected = Request("POST", "/telegram/miniapp/notifications/dismiss", "{\"eventId\":\"same-event\",\"userId\":99}");
        await pipeline(injected);
        Assert.Equal(400, injected.Response.StatusCode);
        var dismiss = Request("POST", "/telegram/miniapp/notifications/dismiss", "{\"eventId\":\"same-event\"}");
        await pipeline(dismiss);
        Assert.Equal(202, dismiss.Response.StatusCode);
        await ReactionWait.UntilAsync(async () => await own.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single((await own.Read()).Items).Dismissed);
        Assert.False(Assert.Single((await other.Read()).Items).Dismissed);

        DefaultHttpContext Request(string method, string path, string body)
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = path;
            context.Request.Method = method;
            context.Request.Headers.Authorization = "tma " + SignedData(Now.ToUnixTimeSeconds());
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Response.Body = new MemoryStream();
            return context;
        }
    }

    [Fact]
    public async Task Surface_rejects_unverified_requests_before_resolving_any_grains()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        new TelegramHttpSurface(new TelegramOptions { BotToken = "token", WebhookSecret = "secret", MiniAppRoot = "" }).Map(app);
        app.Run(context => { context.Response.StatusCode = 418; return Task.CompletedTask; });
        var pipeline = app.Build();
        foreach (var path in new[] { "/telegram/health", "/telegram/webhook", "/telegram/miniapp/state", "/telegram/miniapp/notifications/dismiss", "/telegram/miniapp/reminders/cancel" })
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = path;
            context.Request.Method = path.EndsWith("state", StringComparison.Ordinal) || path.EndsWith("health", StringComparison.Ordinal) ? "GET" : "POST";
            await pipeline(context);
            Assert.Equal(401, context.Response.StatusCode);
        }
        var unrelated = new DefaultHttpContext { RequestServices = services };
        unrelated.Request.Path = "/telegram/anything";
        await pipeline(unrelated);
        Assert.Equal(418, unrelated.Response.StatusCode);
    }

    [Fact]
    public async Task Private_receipts_are_deduplicated_and_wrong_scope_is_rejected()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TelegramModule)]) });
        var source = Source(brain);
        var command = new TelegramMessage(CommandId.New(), "receipt", 42, "Call Alice", Now.ToUnixTimeSeconds(), "UTC");
        await source.Accept(command);
        await ReactionWait.ForSignalAsync(source, TelegramModule.MessageReceivedSignal, TestContext.Current.CancellationToken);
        await source.Accept(command with { Id = CommandId.New() });
        await ReactionWait.UntilAsync(async () => await source.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, (await source.Read()).PublishedCount);
        Assert.Single((await source.ReadJournal(JournalKind.Outgoing, 0)).Delta, entry => entry.Signal.Type == TelegramModule.MessageReceivedSignal);
        await Assert.ThrowsAsync<CommandRejectedException>(() => source.Accept(command with { Id = CommandId.New(), UserId = 99 }));
    }

    [Fact]
    public async Task Authenticated_health_identifies_instance_and_reports_missing_bundle()
    {
        var status = new TelegramConnectionStatus();
        using var services = new ServiceCollection().AddSingleton(status).BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        new TelegramHttpSurface(new TelegramOptions { BotToken = "123:test", WebhookSecret = "secret", MiniAppRoot = "" }).Map(app);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = "/telegram/health";
        context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = "secret";
        context.Response.Body = new MemoryStream();
        await app.Build()(context);
        Assert.Equal(200, context.Response.StatusCode);
        using var response = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal(status.InstanceId, response.RootElement.GetProperty("instanceId").GetString());
        Assert.False(response.RootElement.GetProperty("miniAppAvailable").GetBoolean());
    }

    [Fact]
    public async Task Provider_receipt_memory_survives_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-telegram", Guid.NewGuid().ToString("N"));
        var command = new TelegramMessage(CommandId.New(), "persisted", 42, "Remember", Now.ToUnixTimeSeconds(), "UTC");
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TelegramModule)]), PersistenceDirectory = directory }))
        {
            await Source(brain).Accept(command);
            await ReactionWait.ForSignalAsync(Source(brain), TelegramModule.MessageReceivedSignal, TestContext.Current.CancellationToken);
            await ReactionWait.UntilAsync(async () => await Source(brain).ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        }
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TelegramModule)]), PersistenceDirectory = directory }))
        {
            await Source(brain).Accept(command with { Id = CommandId.New() });
            await ReactionWait.UntilAsync(async () => await Source(brain).ReadPendingCount() == 0, TestContext.Current.CancellationToken);
            Assert.Equal(1, (await Source(brain).Read()).PublishedCount);
        }
    }

    private static ITelegram Source(BrainSimulation brain) => brain.Grains.GetGrain<ITelegram>(new NeuronId("telegram", "42").ToGrainId());
    private static string Update(long chat = 42, string type = "private", bool bot = false) => JsonSerializer.Serialize(new
    {
        update_id = 123, message = new { from = new { id = 42, is_bot = bot }, chat = new { id = chat, type }, text = "hello", date = 1800000000 },
    });
    private static string SignedData(long date)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        { ["auth_date"] = date.ToString(CultureInfo.InvariantCulture), ["user"] = "{\"id\":42}", ["signature"] = "extra" };
        var key = HMACSHA256.HashData("WebAppData"u8, Encoding.UTF8.GetBytes("test-token"));
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(string.Join('\n', values.Select(pair => $"{pair.Key}={pair.Value}"))));
        return string.Join('&', values.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}")) + "&hash=" + Convert.ToHexStringLower(hash);
    }
}
