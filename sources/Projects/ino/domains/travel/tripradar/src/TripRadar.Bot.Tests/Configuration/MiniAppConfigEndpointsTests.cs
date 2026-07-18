using TripRadar.Bot.Configuration;

namespace TripRadar.Bot.Tests.Configuration;

public class MiniAppConfigEndpointsTests
{
    [Fact]
    public void BuildConfig_ConfiguredWebsiteUrl_ReturnsUrl()
    {
        var options = new BotOptions { WebsiteUrl = "https://app.tripradar.io" };

        var config = MiniAppConfigEndpoints.BuildConfig(options);

        config.WebsiteUrl.Should().Be("https://app.tripradar.io");
    }

    [Fact]
    public void BuildConfig_NoWebsiteUrl_ReturnsEmptyString()
    {
        var options = new BotOptions();

        var config = MiniAppConfigEndpoints.BuildConfig(options);

        config.WebsiteUrl.Should().BeEmpty();
    }
}
