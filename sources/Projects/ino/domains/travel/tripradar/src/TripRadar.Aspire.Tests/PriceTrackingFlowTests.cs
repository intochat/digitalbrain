using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TripRadar.Aspire.Tests;

[Collection(AspireDistributedAppCollection.Name)]
public sealed class PriceTrackingFlowTests(ITestOutputHelper output)
{
    private const string FlightsTopic = "Flights";
    private static readonly TimeSpan ResourceReadyTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task FlightPriceChange_OnKafkaEvent_SendsTelegramNotification()
    {
        var telegramUserId = 900_000_000L + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000;
        var username = $"tg_{telegramUserId}";
        var chatId = telegramUserId;
        var consumerGroupId = $"bot-test-{Guid.NewGuid():N}";

        await using var telegram = await FakeTelegramServer.StartAsync();
        output.WriteLine($"Fake Telegram listening at {telegram.BaseUrl} — test user {username} chatId={chatId} group={consumerGroupId}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire>(
            args: [],
            configureBuilder: (_, hostSettings) =>
            {
                hostSettings.Configuration ??= new ConfigurationManager();
                hostSettings.Configuration["Parameters:telegram-bot-token"] = "test-bot-token";
                hostSettings.Configuration["Parameters:telegram-session-sync-secret"] = "test-session-sync-secret";
            });

        foreach (var resourceName in new[] { "api", "jobs" })
        {
            var resource = (ProjectResource)builder.Resources.Single(r => r.Name == resourceName);
            builder.CreateResourceBuilder(resource).WithEnvironment("MockApi__SerpApi", "true");
        }

        var botResource = (ProjectResource)builder.Resources.Single(r => r.Name == "bot");
        builder
            .CreateResourceBuilder(botResource)
            .WithEnvironment("Bot__TelegramApiBaseUrl", telegram.BaseUrl)
            .WithEnvironment("KafkaConsumer__GroupId", consumerGroupId);

        await using var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token).WaitAsync(ResourceReadyTimeout, cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("bot", cts.Token).WaitAsync(ResourceReadyTimeout, cts.Token);

        using var botClient = app.CreateHttpClient("bot");
        botClient.Timeout = TimeSpan.FromSeconds(30);

        var registerResponse = await botClient.PostAsJsonAsync(
            "/api/dev/register-tracking-user",
            new { Username = username, ChatId = chatId },
            cts.Token);
        registerResponse.EnsureSuccessStatusCode();

        var kafkaBootstrap = await app.GetConnectionStringAsync("kafka", cts.Token)
            ?? throw new InvalidOperationException("Kafka connection string not available");

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafkaBootstrap,
            Acks = Acks.All,
            MessageTimeoutMs = 30_000
        }).Build();

        await producer.ProduceAsync(FlightsTopic, BuildFlightEvent(username, price: 500m), cts.Token);
        producer.Flush(TimeSpan.FromSeconds(10));

        // Let the consumer observe the baseline before producing the price change.
        await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

        await producer.ProduceAsync(FlightsTopic, BuildFlightEvent(username, price: 380m), cts.Token);
        producer.Flush(TimeSpan.FromSeconds(10));

        var sendMessageCall = await telegram.WaitForMethodAsync("sendMessage", NotificationTimeout, cts.Token);

        sendMessageCall.Should().NotBeNull("bot should invoke Telegram sendMessage after a price change");
        sendMessageCall!.Body.Should().NotBeNull();
        sendMessageCall.Body!["chat_id"]!.GetValue<long>().Should().Be(chatId);
        sendMessageCall.Body!["text"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    private static Message<string, string> BuildFlightEvent(string username, decimal price)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)).ToString("yyyy-MM-dd");
        var json = $$"""
        {
          "eventId": "{{Guid.NewGuid()}}",
          "eventDate": "{{DateTimeOffset.UtcNow:O}}",
          "eventOwner": { "username": "{{username}}" },
          "eventData": {
            "search_parameters": {
              "departure_id": "PRG",
              "arrival_id": "BCN",
              "outbound_date": "{{date}}"
            },
            "best_flights": [{ "price": {{price}} }]
          }
        }
        """;

        return new Message<string, string>
        {
            Key = $"e2e-{username}",
            Value = json
        };
    }
}
