using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Handlers;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Tests.Notifications.Handlers;

public class LocalPlacesQueryHandlerTests
{
    private readonly TrackingRegistry _registry = new();
    private readonly Mock<INotificationDispatcher> _dispatcher = new();
    private readonly LocalPlacesQueryHandler _sut;

    public LocalPlacesQueryHandlerTests()
    {
        var linkBuilder = new MiniAppLinkBuilder(Options.Create(new BotOptions { MiniAppUrl = "https://app" }));
        _sut = new LocalPlacesQueryHandler(_registry, _dispatcher.Object, linkBuilder, NullLogger<LocalPlacesQueryHandler>.Instance);
    }

    private static string BuildEvent(string username, string topPlaceId, string title = "Trattoria") =>
        $$"""
        {
          "eventId": "{{Guid.NewGuid()}}",
          "eventOwner": { "username": "{{username}}" },
          "eventData": {
            "search_parameters": {
              "q": "italian food",
              "location_requested": "Rome"
            },
            "local_results": [
              {
                "place_id": "{{topPlaceId}}",
                "title": "{{title}}",
                "rating": 4.7,
                "address": "Trastevere"
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task FirstEvent_CreatesBaselineWithPlaceId()
    {
        _registry.RegisterUser("alice", 100L);

        await _sut.HandleAsync(BuildEvent("alice", "place-1"), CancellationToken.None);

        _registry.TryGetSnapshot("alice", ServiceType.LocalPlaces, out var snap).Should().BeTrue();
        snap.Payload.Should().Be("place-1");
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NewTopPlace_DispatchesEnvelope()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", "place-1"), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", "place-2", "Pizzeria"), CancellationToken.None);

        _dispatcher.Verify(d => d.SendAsync(
            100L,
            It.Is<NotificationEnvelope>(e =>
                e.TypeLabel == NotificationStrings.TypeLabels.LocalPlaces
                && e.MainResult == "Найден новый вариант"
                && e.Details.Any(x => x.Contains("Pizzeria"))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SamePlaceId_NoNotification()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", "place-1"), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", "place-1"), CancellationToken.None);

        _dispatcher.VerifyNoOtherCalls();
    }
}
