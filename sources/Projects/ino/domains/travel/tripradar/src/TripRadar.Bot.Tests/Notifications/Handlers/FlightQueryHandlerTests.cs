using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Handlers;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Tests.Notifications.Handlers;

public class FlightQueryHandlerTests
{
    private readonly TrackingRegistry _registry = new();
    private readonly Mock<INotificationDispatcher> _dispatcher = new();
    private readonly FlightQueryHandler _sut;

    public FlightQueryHandlerTests()
    {
        var linkBuilder = new MiniAppLinkBuilder(Options.Create(new BotOptions { MiniAppUrl = "https://app" }));
        _sut = new FlightQueryHandler(_registry, _dispatcher.Object, linkBuilder, NullLogger<FlightQueryHandler>.Instance);
    }

    private static string BuildEvent(string username, decimal price, string dep = "CDG", string arr = "FCO", string date = "2026-05-14") =>
        $$"""
        {
          "eventId": "{{Guid.NewGuid()}}",
          "eventDate": "{{DateTimeOffset.UtcNow:O}}",
          "eventOwner": { "username": "{{username}}" },
          "eventData": {
            "search_parameters": {
              "departure_id": "{{dep}}",
              "arrival_id": "{{arr}}",
              "outbound_date": "{{date}}"
            },
            "best_flights": [{ "price": {{price}} }]
          }
        }
        """;

    [Fact]
    public async Task FirstEvent_RegisteredUser_CreatesBaseline_NoNotification()
    {
        _registry.RegisterUser("alice", 100L);

        await _sut.HandleAsync(BuildEvent("alice", 450m), CancellationToken.None);

        _registry.TryGetSnapshot("alice", ServiceType.Flight, out var snap).Should().BeTrue();
        snap.Payload.Should().Be("450");
        _dispatcher.Verify(d => d.SendAsync(It.IsAny<long>(), It.IsAny<NotificationEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SecondEvent_PriceChange_DispatchesEnvelopeWithFlightLabel()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", 450m), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", 380m), CancellationToken.None);

        _dispatcher.Verify(d => d.SendAsync(
            100L,
            It.Is<NotificationEnvelope>(e =>
                e.TypeLabel == NotificationStrings.TypeLabels.Flight
                && e.MainResult.Contains("380")
                && e.RequestSummary.Contains("CDG")
                && e.RequestSummary.Contains("FCO")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SamePrice_NoNotification()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", 450m), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", 450m), CancellationToken.None);

        _dispatcher.Verify(d => d.SendAsync(It.IsAny<long>(), It.IsAny<NotificationEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnregisteredUser_Skipped()
    {
        await _sut.HandleAsync(BuildEvent("stranger", 450m), CancellationToken.None);

        _registry.TryGetSnapshot("stranger", ServiceType.Flight, out _).Should().BeFalse();
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PriceIncrease_DispatchesNotification()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", 300m), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", 420m), CancellationToken.None);

        _dispatcher.Verify(d => d.SendAsync(
            100L,
            It.Is<NotificationEnvelope>(e => e.MainResult.Contains("420")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
