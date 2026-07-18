using FluentAssertions;
using Ino.NeuronTesting;
using Xunit;

namespace Ino.Domains.Travel.SmokeTests;

public sealed class TripPlannerSmokeTests(NeuronAppHostFixture<Projects.Ino_AppHost_Testing> fixture)
    : TravelNeuronTest<Ino.Domains.Travel.TripPlanner.TripPlanner>(fixture)
{
    [Fact]
    public async Task Bali_initial_card_emits_intro_content_type()
    {
        await using var s = Open();
        await s.Chat("plan a trip to Bali next month");

        s.Last.ContentType.Should().Be("rfw/ino.travel.intro");
        s.Last.Rfw.Should().NotBeNull();
        s.Last.Rfw!.ContainsWidgets("WeatherSummaryCard", "FlightCard").Should().BeTrue();
    }
}
