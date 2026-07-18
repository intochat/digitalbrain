using Microsoft.Extensions.Options;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Tests.Telegram;

public class MiniAppLinkBuilderTests
{
    private static MiniAppLinkBuilder Build(string miniAppUrl = "https://app.tripradar.io")
        => new(Options.Create(new BotOptions { MiniAppUrl = miniAppUrl }));

    [Theory]
    [InlineData(ServiceType.Flight, "flights/results")]
    [InlineData(ServiceType.Hotel, "hotels/results")]
    [InlineData(ServiceType.LocalPlaces, "places/results")]
    [InlineData(ServiceType.Event, "events/results")]
    public void ForResult_BuildsTypedDeepLink(ServiceType type, string expectedPath)
    {
        var builder = Build();
        var requestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var url = builder.ForResult(type, requestId);

        url.Should().Be($"https://app.tripradar.io/{expectedPath}?requestId={requestId}");
    }

    [Fact]
    public void ForResult_NullRequestId_FallsBackToAlertsScreen()
    {
        var builder = Build();

        var url = builder.ForResult(ServiceType.Flight, null);

        url.Should().Be("https://app.tripradar.io/alerts");
    }

    [Fact]
    public void ForResult_EmptyRequestId_FallsBackToAlertsScreen()
    {
        var builder = Build();

        var url = builder.ForResult(ServiceType.Hotel, Guid.Empty);

        url.Should().Be("https://app.tripradar.io/alerts");
    }

    [Fact]
    public void ForResult_EmptyMiniAppUrl_ReturnsEmpty()
    {
        var builder = Build("");

        builder.ForResult(ServiceType.Flight, Guid.NewGuid()).Should().BeEmpty();
        builder.ForAlertsScreen().Should().BeEmpty();
    }

    [Fact]
    public void ForResult_TrimsTrailingSlashFromBaseUrl()
    {
        var builder = Build("https://app.tripradar.io/");
        var requestId = Guid.NewGuid();

        var url = builder.ForResult(ServiceType.Event, requestId);

        url.Should().Be($"https://app.tripradar.io/events/results?requestId={requestId}");
    }
}
