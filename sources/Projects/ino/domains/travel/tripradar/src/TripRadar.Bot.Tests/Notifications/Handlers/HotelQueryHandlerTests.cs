using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Handlers;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Tests.Notifications.Handlers;

public class HotelQueryHandlerTests
{
    private readonly TrackingRegistry _registry = new();
    private readonly Mock<INotificationDispatcher> _dispatcher = new();
    private readonly HotelQueryHandler _sut;

    public HotelQueryHandlerTests()
    {
        var linkBuilder = new MiniAppLinkBuilder(Options.Create(new BotOptions { MiniAppUrl = "https://app" }));
        _sut = new HotelQueryHandler(_registry, _dispatcher.Object, linkBuilder, NullLogger<HotelQueryHandler>.Instance);
    }

    private static string BuildEvent(string username, params decimal[] propertyRates) =>
        $$"""
        {
          "eventId": "{{Guid.NewGuid()}}",
          "eventOwner": { "username": "{{username}}" },
          "eventData": {
            "search_parameters": {
              "query": "Rome",
              "check_in_date": "2026-05-14",
              "check_out_date": "2026-05-17"
            },
            "properties": [
              {{string.Join(",", propertyRates.Select((r, i) => $$"""
              {
                "name": "Hotel{{i}}",
                "overall_rating": 4.5,
                "rate_per_night": { "extracted_lowest": {{r}} }
              }
              """))}}
            ]
          }
        }
        """;

    [Fact]
    public async Task FirstEvent_CreatesBaselineWithCheapestRate()
    {
        _registry.RegisterUser("alice", 100L);

        await _sut.HandleAsync(BuildEvent("alice", 250m, 180m, 320m), CancellationToken.None);

        _registry.TryGetSnapshot("alice", ServiceType.Hotel, out var snap).Should().BeTrue();
        snap.Payload.Should().Be("180");
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PriceChange_DispatchesEnvelopeWithHotelLabel()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", 200m), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", 150m), CancellationToken.None);

        _dispatcher.Verify(d => d.SendAsync(
            100L,
            It.Is<NotificationEnvelope>(e =>
                e.TypeLabel == NotificationStrings.TypeLabels.Hotel
                && e.MainResult.Contains("150")
                && e.RequestSummary.Contains("Rome")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SamePrice_NoNotification()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", 200m), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", 200m), CancellationToken.None);

        _dispatcher.VerifyNoOtherCalls();
    }
}
