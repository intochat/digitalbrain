using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Handlers;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Tests.Notifications.Handlers;

public class EventQueryHandlerTests
{
    private readonly TrackingRegistry _registry = new();
    private readonly Mock<INotificationDispatcher> _dispatcher = new();
    private readonly EventQueryHandler _sut;

    public EventQueryHandlerTests()
    {
        var linkBuilder = new MiniAppLinkBuilder(Options.Create(new BotOptions { MiniAppUrl = "https://app" }));
        _sut = new EventQueryHandler(_registry, _dispatcher.Object, linkBuilder, NullLogger<EventQueryHandler>.Instance);
    }

    private static string BuildEvent(string username, string title, string startDate = "2026-05-15") =>
        $$"""
        {
          "eventId": "{{Guid.NewGuid()}}",
          "eventOwner": { "username": "{{username}}" },
          "eventData": {
            "search_parameters": { "query": "concerts in Rome" },
            "events_results": [
              {
                "title": "{{title}}",
                "date": { "start_date": "{{startDate}}", "when": "20:00" },
                "address": ["Auditorium", "Rome"],
                "venue": { "name": "Auditorium Parco della Musica" }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task FirstEvent_CreatesBaselineFingerprint()
    {
        _registry.RegisterUser("alice", 100L);

        await _sut.HandleAsync(BuildEvent("alice", "Jazz Night"), CancellationToken.None);

        _registry.TryGetSnapshot("alice", ServiceType.Event, out var snap).Should().BeTrue();
        snap.Payload.Should().Contain("Jazz Night");
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NewTopEvent_DispatchesEnvelope()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", "Jazz Night"), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", "Rock Festival"), CancellationToken.None);

        _dispatcher.Verify(d => d.SendAsync(
            100L,
            It.Is<NotificationEnvelope>(e =>
                e.TypeLabel == NotificationStrings.TypeLabels.Event
                && e.MainResult.Contains("Rock Festival")
                && e.RequestSummary.Contains("concerts in Rome")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SameTitleAndDate_NoNotification()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", "Jazz Night"), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", "Jazz Night"), CancellationToken.None);

        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SameTitleDifferentDate_DispatchesEnvelope()
    {
        _registry.RegisterUser("alice", 100L);
        await _sut.HandleAsync(BuildEvent("alice", "Jazz Night", "2026-05-15"), CancellationToken.None);

        await _sut.HandleAsync(BuildEvent("alice", "Jazz Night", "2026-06-20"), CancellationToken.None);

        _dispatcher.Verify(d => d.SendAsync(
            It.IsAny<long>(),
            It.IsAny<NotificationEnvelope>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
