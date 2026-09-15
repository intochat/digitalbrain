using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Telegram;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TelegramBehaviorFacts
{
    private const long User = 81723;
    private static readonly NeuronId Inbox = new("notification", $"telegram-{User}");
    private static readonly NeuronId ReminderBook = new("reminders", $"telegram-{User}");

    [Fact]
    public async Task Authenticated_provider_webhook_installs_behavior_and_reacts_to_message()
    {
        using var model = new ScriptedChatClient();
        model.Say(JsonSerializer.Serialize(new { intent = "reminder", text = "Call Alice", dueUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() }));
        await using var brain = await Start(model);
        var app = new ApplicationBuilder(brain.SiloServices);
        new TelegramHttpSurface(new TelegramOptions { BotToken = "123:test", WebhookSecret = "secret", MiniAppRoot = "" }).Map(app);
        var pipeline = app.Build();
        var body = JsonSerializer.Serialize(new
        {
            update_id = 123, message = new { from = new { id = User, is_bot = false }, chat = new { id = User, type = "private" },
                text = "Remind me in ten minutes to call Alice", date = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
        });
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var context = new DefaultHttpContext { RequestServices = brain.SiloServices };
            context.Request.Method = "POST";
            context.Request.Path = "/telegram/webhook";
            context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = "secret";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Response.Body = new MemoryStream();
            await pipeline(context);
            Assert.Equal(200, context.Response.StatusCode);
        }
        var inbox = brain.Grains.GetGrain<INotification>(Inbox.ToGrainId());
        var reminders = brain.Grains.GetGrain<IReminders>(ReminderBook.ToGrainId());
        await ReactionWait.UntilAsync(async () => (await inbox.Read()).Items.Count == 1 && (await reminders.Read()).Items.Count == 1, TestContext.Current.CancellationToken);
        Assert.Equal("Call Alice", Assert.Single((await reminders.Read()).Items).Text);
        Assert.Single(model.Calls);
        Assert.NotNull(brain.SiloServices.GetRequiredService<TelegramConnectionStatus>().Read().LastMessageAt);
    }

    [Fact]
    public async Task Message_creates_one_reminder_and_due_notification_after_cold_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        string? run;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var model = new ScriptedChatClient())
        {
            await using var brain = await Start(model, directory);
            await brain.SiloServices.GetRequiredService<ITelegramBehaviorSetup>().EnsureAsync(User, TestContext.Current.CancellationToken);
            now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            model.Say(JsonSerializer.Serialize(new { intent = "reminder", text = "Call Alice", dueUnixSeconds = now + 8 }));
            var behavior = brain.Grains.GetGrain<IBehavior>(TelegramReminderBehavior.Identity(User).ToGrainId());
            run = (await behavior.Read()).RunId;
            var source = brain.Grains.GetGrain<ITelegram>(new NeuronId("telegram", User.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToGrainId());
            await source.Accept(new(CommandId.New(), "update-1", User, "Remind me in eight seconds to call Alice", now, "UTC"));
            var reminders = brain.Grains.GetGrain<IReminders>(ReminderBook.ToGrainId());
            var inbox = brain.Grains.GetGrain<INotification>(Inbox.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await reminders.Read()).Items.Count == 1 && (await inbox.Read()).Items.Count == 1, TestContext.Current.CancellationToken);
            Assert.Equal("Call Alice", Assert.Single((await reminders.Read()).Items).Text);
            await source.Accept(new(CommandId.New(), "update-1", User, "Remind me in eight seconds to call Alice", now, "UTC"));
            await ReactionWait.UntilAsync(async () => await source.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
            Assert.Single(model.Calls);
            Assert.Single((await reminders.Read()).Items);
        }

        using (var model = new ScriptedChatClient())
        {
            await using var brain = await Start(model, directory);
            await brain.SiloServices.GetRequiredService<ITelegramBehaviorSetup>().EnsureAsync(User, TestContext.Current.CancellationToken);
            var behavior = brain.Grains.GetGrain<IBehavior>(TelegramReminderBehavior.Identity(User).ToGrainId());
            Assert.Equal(run, (await behavior.Read()).RunId);
            var inbox = brain.Grains.GetGrain<INotification>(Inbox.ToGrainId());
            await ReactionWait.UntilAsync(async () => (await inbox.Read()).Items.Any(item => item.Kind == "reminder"), TestContext.Current.CancellationToken);
            var items = (await inbox.Read()).Items;
            Assert.Equal(2, items.Count);
            Assert.Equal("Call Alice", Assert.Single(items, item => item.Kind == "reminder").Message);
            Assert.Empty(model.Calls);
        }
    }

    [Fact]
    public async Task Ordinary_messages_do_not_schedule_ambiguous_requests_clarify_and_manual_stop_is_respected()
    {
        using var model = new ScriptedChatClient();
        model.Say("""{"intent":"other","text":"","dueUnixSeconds":0}""");
        model.Say("""{"intent":"clarify","text":"","dueUnixSeconds":0}""");
        await using var brain = await Start(model);
        var setup = brain.SiloServices.GetRequiredService<ITelegramBehaviorSetup>();
        await setup.EnsureAsync(User, TestContext.Current.CancellationToken);
        var source = brain.Grains.GetGrain<ITelegram>(new NeuronId("telegram", User.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToGrainId());
        var inbox = brain.Grains.GetGrain<INotification>(Inbox.ToGrainId());
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await source.Accept(new(CommandId.New(), "ordinary", User, "Hello", now, "UTC"));
        await ReactionWait.UntilAsync(async () => model.Calls.Count == 1 && (await inbox.Read()).Items.Count == 1, TestContext.Current.CancellationToken);
        await source.Accept(new(CommandId.New(), "ambiguous", User, "Remind me to call Alice", now, "UTC"));
        await ReactionWait.UntilAsync(async () => (await inbox.Read()).Items.Count == 3, TestContext.Current.CancellationToken);
        Assert.Equal("Please send the reminder again with a clear future date and time.",
            Assert.Single((await inbox.Read()).Items, item => item.Kind == "clarification").Message);
        Assert.Empty((await brain.Grains.GetGrain<IReminders>(ReminderBook.ToGrainId()).Read()).Items);

        var behavior = brain.Grains.GetGrain<IBehavior>(TelegramReminderBehavior.Identity(User).ToGrainId());
        await behavior.Stop(new(CommandId.New()));
        await ReactionWait.UntilAsync(async () => (await behavior.Read()).Status == BehaviorStatus.Stopped, TestContext.Current.CancellationToken);
        await setup.EnsureAsync(User, TestContext.Current.CancellationToken);
        Assert.Equal(BehaviorStatus.Stopped, (await behavior.Read()).Status);
        await source.Accept(new(CommandId.New(), "after-stop", User, "Hello again", now, "UTC"));
        await ReactionWait.UntilAsync(async () => await source.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        Assert.Equal(3, (await inbox.Read()).Items.Count);
        Assert.Equal(2, model.Calls.Count);
        Assert.Empty((await brain.Grains.GetGrain<INotification>(new NeuronId("notification", "telegram-999").ToGrainId()).Read()).Items);
    }

    [Fact]
    public async Task Unsupported_deadline_notifies_without_blocking_a_later_valid_reminder()
    {
        using var model = new ScriptedChatClient();
        await using var brain = await Start(model);
        await brain.SiloServices.GetRequiredService<ITelegramBehaviorSetup>().EnsureAsync(User, TestContext.Current.CancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        model.Say(JsonSerializer.Serialize(new { intent = "reminder", text = "Too far away", dueUnixSeconds = now + 2L * 366 * 86400 }));
        model.Say(JsonSerializer.Serialize(new { intent = "reminder", text = "Call Alice", dueUnixSeconds = now + 60 }));
        var source = brain.Grains.GetGrain<ITelegram>(new NeuronId("telegram", User.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToGrainId());
        var inbox = brain.Grains.GetGrain<INotification>(Inbox.ToGrainId());
        var reminders = brain.Grains.GetGrain<IReminders>(ReminderBook.ToGrainId());
        await source.Accept(new(CommandId.New(), "too-far", User, "Remind me in two years", now, "UTC"));
        await ReactionWait.UntilAsync(async () => (await inbox.Read()).Items.Any(item => item.Title == "Reminder was not scheduled"), TestContext.Current.CancellationToken);
        Assert.Empty((await reminders.Read()).Items);
        await source.Accept(new(CommandId.New(), "valid-after-refusal", User, "Remind me in a minute to call Alice", now, "UTC"));
        await ReactionWait.UntilAsync(async () => (await reminders.Read()).Items.Count == 1, TestContext.Current.CancellationToken);
        Assert.Equal("Call Alice", Assert.Single((await reminders.Read()).Items).Text);
        var behavior = brain.Grains.GetGrain<IBehavior>(TelegramReminderBehavior.Identity(User).ToGrainId());
        Assert.All(await behavior.Diagnostics(), diagnostic => Assert.Null(diagnostic.Status.Error));
    }

    private static Task<BrainSimulation> Start(ScriptedChatClient model, string? directory = null)
        => BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(UIModule), typeof(TimeModule), typeof(TelegramModule)]),
            PersistenceDirectory = directory,
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<IChatClient>(model);
                silo.Services.AddSingleton(new TimeOptions { AlarmPeriod = TimeSpan.FromSeconds(1) });
                silo.Services.Configure<ReminderOptions>(options => options.MinimumReminderPeriod = TimeSpan.FromSeconds(1));
            }
        });
}
