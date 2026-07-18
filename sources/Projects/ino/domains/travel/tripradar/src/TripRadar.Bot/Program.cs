using Telegram.BotAPI;
using TripRadar.Bot.Auth;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Handlers;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;
using TripRadar.Bot.TripRadarApi;
using TripRadar.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ScheduledQueryConsumer.MeterName));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));
builder.Services.Configure<KafkaConsumerOptions>(builder.Configuration.GetSection(KafkaConsumerOptions.SectionName));

if (!builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(builder.Configuration[$"{BotOptions.SectionName}:SessionSyncSecret"]))
{
    throw new InvalidOperationException(
        $"{BotOptions.SectionName}:SessionSyncSecret must be configured outside Development — " +
        "the session-sync endpoint refuses traffic without it.");
}

var botToken = builder.Configuration["Bot:BotToken"];
if (!string.IsNullOrWhiteSpace(botToken))
{
    var telegramApiBaseUrl = builder.Configuration["Bot:TelegramApiBaseUrl"];
    builder.Services.AddSingleton<ITelegramBotClient>(_ =>
        string.IsNullOrWhiteSpace(telegramApiBaseUrl)
            ? new TelegramBotClient(botToken)
            : new TelegramBotClient(new TelegramBotClientOptions(botToken)
            {
                ServerAddress = telegramApiBaseUrl
            }));
    builder.Services.AddHostedService<TelegramWebhookSetup>();
}

builder.Services.AddSingleton<IUserSessionStore, UserSessionStore>();
builder.Services.AddTransient<AuthSessionSyncHandler>();

builder.Services.AddHttpClient<ITripRadarTokenClient, TripRadarTokenClient>(client =>
    client.BaseAddress = new Uri("https+http://api"));

builder.Services.AddSingleton<ITelegramBotService, TelegramBotService>();
builder.Services.AddSingleton<MiniAppLinkBuilder>();

builder.Services.AddSingleton<ITrackingRegistry, TrackingRegistry>();
builder.Services.AddSingleton<NotificationEnvelopeRenderer>();
builder.Services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

builder.Services.AddSingleton<IScheduledQueryHandler, FlightQueryHandler>();
builder.Services.AddSingleton<IScheduledQueryHandler, HotelQueryHandler>();
builder.Services.AddSingleton<IScheduledQueryHandler, LocalPlacesQueryHandler>();
builder.Services.AddSingleton<IScheduledQueryHandler, EventQueryHandler>();

var kafkaConsumerGroupId = builder.Configuration
    .GetSection(KafkaConsumerOptions.SectionName)
    .GetValue<string>(nameof(KafkaConsumerOptions.GroupId)) ?? "bot";

builder.AddKafkaConsumer<string, string>("kafka", settings =>
{
    settings.Config.GroupId = kafkaConsumerGroupId;
    settings.Config.EnableAutoCommit = false;

    if (builder.Environment.IsDevelopment())
        settings.Config.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
});
builder.Services.AddHostedService<ScheduledQueryConsumer>();

if (builder.Environment.IsDevelopment())
{
    builder.AddKafkaProducer<string, string>("kafka");
}

builder.Services.AddHttpClient<ITripRadarApiClient, TripRadarApiClient>(client =>
    client.BaseAddress = new Uri("https+http://api"));

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapTelegramEndpoints();
app.MapMiniAppConfigEndpoints();
app.MapReverseProxy();
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await next();
    });
}
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Run();
