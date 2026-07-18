using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Tests.Telegram;

public class TelegramEndpointsTests
{
    private static BotOptions OptionsWithMiniAppUrl(string miniAppUrl = "https://app.tripradar.io", string websiteUrl = "https://www.tripradar.io") =>
        new()
        {
            MiniAppUrl = miniAppUrl,
            WebsiteUrl = websiteUrl,
            WebhookSecretToken = "secret123",
            SessionSyncSecret = "sync-secret",
            InternalApiKey = "internal-key"
        };

    [Fact]
    public async Task ProcessUpdate_StartCommand_NoTelegramUserId_FallsBackToMiniAppLaunch()
    {
        var botService = new Mock<ITelegramBotService>();
        var trackingRegistry = new Mock<ITrackingRegistry>();
        var options = OptionsWithMiniAppUrl();

        var update = new Update
        {
            UpdateId = 1,
            Message = new Message
            {
                MessageId = 10,
                Text = "/start",
                Chat = new Chat { Id = 42 }
            }
        };

        await TelegramEndpoints.ProcessUpdateAsync(update, botService.Object, options, trackingRegistry.Object, NullLogger.Instance);

        botService.Verify(s => s.SendMiniAppLaunchAsync(42L, NotificationStrings.Button, $"{options.MiniAppUrl}/flights", NotificationStrings.Button, It.IsAny<CancellationToken>()), Times.Once);
        trackingRegistry.Verify(r => r.RegisterUser(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task ProcessUpdate_StartCommand_UnregisteredUser_SendsWelcomeWithBothButtons()
    {
        var botService = new Mock<ITelegramBotService>();
        var trackingRegistry = new Mock<ITrackingRegistry>();
        var options = OptionsWithMiniAppUrl();

        var update = new Update
        {
            UpdateId = 2,
            Message = new Message
            {
                MessageId = 20,
                Text = "/start",
                Chat = new Chat { Id = 42 },
                From = new User { Id = 8888, FirstName = "Bob", IsBot = false }
            }
        };

        await TelegramEndpoints.ProcessUpdateAsync(update, botService.Object, options, trackingRegistry.Object, NullLogger.Instance);
        trackingRegistry.Verify(r => r.RegisterUser(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        botService.Verify(s => s.SendWelcomeWithRegistrationAsync(
            42L,
            NotificationStrings.WelcomeUnregistered,
            "https://www.tripradar.io/auth/telegram-google?source=telegram&chatId=42",
            NotificationStrings.ContinueWithGoogle,
            "https://www.tripradar.io/signin?source=telegram&chatId=42",
            NotificationStrings.RegisterOnWebsite,
            It.IsAny<CancellationToken>()), Times.Once);
        botService.Verify(s => s.SendMiniAppLaunchAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessUpdate_NonStartMessage_DoesNotCallSendMiniAppLaunch()
    {
        var botService = new Mock<ITelegramBotService>();
        var options = OptionsWithMiniAppUrl();

        var update = new Update
        {
            UpdateId = 2,
            Message = new Message
            {
                MessageId = 11,
                Text = "hello",
                Chat = new Chat { Id = 99 }
            }
        };

        await TelegramEndpoints.ProcessUpdateAsync(update, botService.Object, options, new Mock<ITrackingRegistry>().Object, NullLogger.Instance);

        botService.Verify(s => s.SendMiniAppLaunchAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessUpdate_CallbackQuery_CallsAnswerCallbackQuery()
    {
        var botService = new Mock<ITelegramBotService>();
        var options = OptionsWithMiniAppUrl();

        var update = new Update
        {
            UpdateId = 3,
            CallbackQuery = new CallbackQuery
            {
                Id = "cq-123",
                Data = "some_data"
            }
        };

        await TelegramEndpoints.ProcessUpdateAsync(update, botService.Object, options, new Mock<ITrackingRegistry>().Object, NullLogger.Instance);

        botService.Verify(s => s.AnswerCallbackQueryAsync("cq-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUpdate_CallbackQuery_DoesNotCallSendMiniAppLaunch()
    {
        var botService = new Mock<ITelegramBotService>();
        var options = OptionsWithMiniAppUrl();

        var update = new Update
        {
            UpdateId = 4,
            CallbackQuery = new CallbackQuery
            {
                Id = "cq-456",
                Data = "btn_action"
            }
        };

        await TelegramEndpoints.ProcessUpdateAsync(update, botService.Object, options, new Mock<ITrackingRegistry>().Object, NullLogger.Instance);

        botService.Verify(s => s.SendMiniAppLaunchAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessUpdate_NullMessageAndNoCallbackQuery_DoesNothing()
    {
        var botService = new Mock<ITelegramBotService>();
        var options = OptionsWithMiniAppUrl();

        var update = new Update { UpdateId = 5 };

        await TelegramEndpoints.ProcessUpdateAsync(update, botService.Object, options, new Mock<ITrackingRegistry>().Object, NullLogger.Instance);

        botService.VerifyNoOtherCalls();
    }

    [Fact]
    public void HasValidWebhookSecret_CorrectHeader_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = "secret123";
        var options = new BotOptions { WebhookSecretToken = "secret123" };

        var result = TelegramEndpoints.HasValidWebhookSecret(context, options, NullLogger.Instance);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasValidWebhookSecret_MissingHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        var options = new BotOptions { WebhookSecretToken = "secret123" };

        var result = TelegramEndpoints.HasValidWebhookSecret(context, options, NullLogger.Instance);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasValidWebhookSecret_WrongHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = "wrong-secret";
        var options = new BotOptions { WebhookSecretToken = "secret123" };

        var result = TelegramEndpoints.HasValidWebhookSecret(context, options, NullLogger.Instance);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasValidWebhookSecret_NoSecretConfigured_AlwaysReturnsTrue()
    {
        var context = new DefaultHttpContext();
        var options = new BotOptions { WebhookSecretToken = "" };

        var result = TelegramEndpoints.HasValidWebhookSecret(context, options, NullLogger.Instance);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasValidSessionSyncSecret_CorrectHeader_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Session-Secret"] = "sync-secret";
        var options = new BotOptions { SessionSyncSecret = "sync-secret" };

        var result = TelegramEndpoints.HasValidSessionSyncSecret(context, options);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasValidSessionSyncSecret_MissingHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        var options = new BotOptions { SessionSyncSecret = "sync-secret" };

        var result = TelegramEndpoints.HasValidSessionSyncSecret(context, options);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasValidSessionSyncSecret_EmptySecretConfigured_FailsClosed()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Session-Secret"] = "anything";
        var options = new BotOptions { SessionSyncSecret = "" };

        var result = TelegramEndpoints.HasValidSessionSyncSecret(context, options);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasValidSessionSyncSecret_WrongHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Telegram-Session-Secret"] = "wrong-secret";
        var options = new BotOptions { SessionSyncSecret = "sync-secret" };

        var result = TelegramEndpoints.HasValidSessionSyncSecret(context, options);

        result.Should().BeFalse();
    }
}
