using System.Diagnostics;
using System.Net.Http.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Cloudflared;
using Aspire.Hosting.TripRadar;
using Aspire.Hosting.TripRadar.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Bot;

internal static class BotExtensions
{
    extension(IDistributedApplicationBuilder builder)
    {
        public IResourceBuilder<ProjectResource> AddBot()
        {
            var telegramParam = builder.Resources.OfType<ParameterResource>()
                .FirstOrDefault(r => r.Name == TripRadarConstants.ParameterNames.TelegramBotToken)
                ?? throw new InvalidOperationException(
                    $"Parameter '{TripRadarConstants.ParameterNames.TelegramBotToken}' not found. Ensure AddTripRadar() is called before AddBot().");
            var botToken = builder.CreateResourceBuilder(telegramParam);
            var sessionSecret = builder.AddParameter(
                TripRadarConstants.ParameterNames.TelegramSessionSyncSecret,
                () => builder.Configuration[TripRadarConstants.Bot.SessionSyncSecretEnvVar] ?? string.Empty,
                secret: true);

            var websiteTunnelLog = CloudflaredExtensions.GetLogFilePath(TripRadarConstants.Bot.WebsiteTunnelName);

            var bot = builder.AddProject<Projects.TripRadar_Bot>("bot")
                .WithEnvironment(TripRadarConstants.Bot.BotToken, botToken)
                .WithEnvironment(TripRadarConstants.Bot.SessionSyncSecret, sessionSecret)
                .WithCloudflaredTunnel("cloudflared", TripRadarConstants.Bot.Port)
                .WithEnvironment(context =>
                {
                    var websiteUrl = CloudflaredExtensions.ExtractTunnelUrl(websiteTunnelLog);
                    if (websiteUrl is not null)
                        context.EnvironmentVariables[TripRadarConstants.Bot.WebsiteUrl] = websiteUrl;
                });

            if (builder.Environment.IsDevelopment())
            {
                bot.WithHttpCommand(
                    path: "/api/dev/simulate-price-events",
                    displayName: "Simulate Price Drop",
                    commandName: "simulate-price-drop",
                    commandOptions: new HttpCommandOptions
                    {
                        Method = HttpMethod.Post,
                        Description = "Publishes 2 Kafka events: baseline $500, drop to $380. Check bot logs for alert.",
                        ConfirmationMessage = "This will register dev user tg_100002 and publish 2 price events to Kafka. Continue?",
                        IconName = "ArrowTrendingDown",
                        PrepareRequest = ctx =>
                        {
                            ctx.Request.Content = JsonContent.Create(new { prices = new[] { 500, 380 } });
                            return Task.CompletedTask;
                        }
                    });

                bot.WithHttpCommand(
                    path: "/api/dev/simulate-price-events",
                    displayName: "Simulate Price Increase",
                    commandName: "simulate-price-increase",
                    commandOptions: new HttpCommandOptions
                    {
                        Method = HttpMethod.Post,
                        Description = "Publishes 2 Kafka events: baseline $300, increase to $450. Check bot logs for alert.",
                        ConfirmationMessage = "This will register dev user tg_100002 and publish 2 price events to Kafka. Continue?",
                        IconName = "ArrowTrending",
                        PrepareRequest = ctx =>
                        {
                            ctx.Request.Content = JsonContent.Create(new { prices = new[] { 300, 450 } });
                            return Task.CompletedTask;
                        }
                    });

                bot.WithHttpCommand(
                    path: "/api/dev/simulate-price-events",
                    displayName: "Simulate Price Series",
                    commandName: "simulate-price-series",
                    commandOptions: new HttpCommandOptions
                    {
                        Method = HttpMethod.Post,
                        Description = "Publishes 5 Kafka events simulating price volatility. Check bot logs for alerts.",
                        ConfirmationMessage = "This will register dev user tg_100002 and publish 5 price events to Kafka. Continue?",
                        IconName = "ArrowTrendingLines",
                        PrepareRequest = ctx =>
                        {
                            ctx.Request.Content = JsonContent.Create(new { prices = new[] { 500, 380, 420, 350, 520 } });
                            return Task.CompletedTask;
                        }
                    });

                bot.WithDevLoginCommand("login-as-basic",     "Login as Basic user",     identifier: "100001",                                tier: null,        iconName: "Person");
                bot.WithDevLoginCommand("login-as-essential", "Login as Essential user", identifier: "100002",                                tier: "essential", iconName: "Star");
                bot.WithDevLoginCommand("login-as-advanced",  "Login as Advanced user",  identifier: "100003",                                tier: "advanced",  iconName: "Diamond");

                var devHandle = builder.Configuration[$"Parameters:{TripRadarConstants.ParameterNames.DevTelegramHandle}"];
                var devUserId = builder.Configuration[$"Parameters:{TripRadarConstants.ParameterNames.DevTelegramUserId}"];
                var devIdentifier = !string.IsNullOrWhiteSpace(devHandle) ? devHandle : devUserId;
                if (!string.IsNullOrWhiteSpace(devIdentifier))
                {
                    bot.WithDevLoginCommand(
                        commandName: "login-as-me",
                        displayName: $"Login as me ({devIdentifier})",
                        identifier: devIdentifier,
                        tier: "advanced",
                        iconName: "PersonAccounts");

                    var trimmedDevIdentifier = devIdentifier.Trim().TrimStart('@');
                    var targetUsername = long.TryParse(trimmedDevIdentifier, out var numericDevId) && numericDevId > 0
                        ? $"tg_{numericDevId}"
                        : trimmedDevIdentifier;

                    bot.WithHttpCommand(
                        path: "/api/dev/simulate-price-events",
                        displayName: $"Notify me now ({devIdentifier})",
                        commandName: "notify-me-now",
                        commandOptions: new HttpCommandOptions
                        {
                            Method = HttpMethod.Post,
                            Description = $"Publishes 2 Kafka events (baseline $500, drop to $380) targeting {devIdentifier}. Requires /start in your real Telegram first so the bot has your chatId.",
                            ConfirmationMessage = $"Publish Kafka price-drop events for {devIdentifier}? The bot must have received /start from you in real Telegram so it knows your chatId.",
                            IconName = "Alert",
                            PrepareRequest = ctx =>
                            {
                                ctx.Request.Content = JsonContent.Create(new
                                {
                                    username = targetUsername,
                                    prices = new[] { 500, 380 }
                                });
                                return Task.CompletedTask;
                            }
                        });
                }
            }

            return bot;
        }
    }

    private static IResourceBuilder<ProjectResource> WithDevLoginCommand(
        this IResourceBuilder<ProjectResource> bot,
        string commandName,
        string displayName,
        string identifier,
        string? tier,
        string iconName)
    {
        var trimmed = identifier.Trim().TrimStart('@');
        var query = long.TryParse(trimmed, out var numericId) && numericId > 0
            ? $"dev_login_as={numericId}&redirect=/flights"
            : $"dev_login_as_handle={Uri.EscapeDataString(trimmed)}&redirect=/flights";

        if (!string.IsNullOrWhiteSpace(tier))
            query += $"&tier={Uri.EscapeDataString(tier)}";

        var url = $"http://localhost:{TripRadarConstants.Bot.Port}/auth?{query}";
        var description = tier is null
            ? $"Open MiniApp logged in as Telegram {identifier} (no paid tier)."
            : $"Open MiniApp logged in as Telegram {identifier} with tier '{tier}'.";

        return bot.WithCommand(
            name: commandName,
            displayName: displayName,
            executeCommand: ctx =>
            {
                var logger = ctx.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Aspire.Bot.DevLogin");
                logger.LogInformation("Opening MiniApp dev login URL: {Url}", url);

                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return Task.FromResult(CommandResults.Success());
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to launch browser for {Url}", url);
                    return Task.FromResult(CommandResults.Failure($"Failed to open browser: {ex.Message}"));
                }
            },
            commandOptions: new CommandOptions
            {
                Description = description,
                IconName = iconName,
                IconVariant = IconVariant.Filled,
                IsHighlighted = false
            });
    }

    extension(IResourceBuilder<ProjectResource> bot)
    {
        public IResourceBuilder<ProjectResource> WithReference(TripRadarResource server) =>
            bot.WithReference(server.ToServices());

        public IResourceBuilder<ProjectResource> WithReference(TripRadarServices server) =>
            bot.WithReference(server.Api)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.TripRadarApiApiKey, server.ApiKey)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.TripRadarApiBearerToken, server.GraphQlBearerToken)
                .WithEnvironment(TripRadarConstants.Bot.InternalApiKey, server.InternalApiKey);

        public IResourceBuilder<ProjectResource> WithReference(IResourceBuilder<KafkaServerResource> kafka) =>
            bot.WithReference(kafka, connectionName: TripRadarConstants.ConnectionNames.Kafka)
                .WaitFor(kafka);
    }
}
