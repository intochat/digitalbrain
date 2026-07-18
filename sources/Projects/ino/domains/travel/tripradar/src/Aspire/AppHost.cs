using Aspire.Hosting.Bot;
using Aspire.Hosting.TripRadar;
using Aspire.Hosting.TripRadar.Constants;
using Aspire.Hosting.Website;

var builder = DistributedApplication.CreateBuilder(args);

var kafka = builder.AddKafka("kafka")
    .WithKafkaUI(container => container.WithLifetime(ContainerLifetime.Persistent), containerName: "kafka-ui")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var server = builder.AddTripRadar(kafka)
    .WithRealApis();

var bot = builder.AddBot()
    .WithReference(kafka)
    .WithReference(server)
    .WaitFor(server);

// API calls bot to notify the user's Telegram chat when they sign in on the website.
var sessionSyncSecret = builder.Resources.OfType<ParameterResource>()
    .First(r => r.Name == TripRadarConstants.ParameterNames.TelegramSessionSyncSecret);
server.Api
    .WithReference(bot)
    .WithEnvironment("Bot__SessionSyncSecret", builder.CreateResourceBuilder(sessionSyncSecret));

builder.AddWebsite()
    .WithReference(server)
    .WaitFor(server);

builder.Build().Run();
