using Ino.Domains.Travel.FlightSearch;
using Ino.Domains.Travel.HotelSearch;
using Ino.Domains.Travel.PlaceSearch;
using Xunit;

namespace Ino.Domains.Tests;

public sealed class TravelFixturesTests
{
    [Theory]
    [InlineData("Tokyo")]
    [InlineData("Paris")]
    [InlineData("NYC")]
    [InlineData("tokyo")]   // case-insensitive lookup
    [InlineData("paris")]
    [InlineData("nyc")]
    public void Flight_hotel_place_fixtures_return_data_for_demo_destinations(string destination)
    {
        Assert.NotEmpty(FlightFixture.For(destination));
        Assert.NotEmpty(HotelFixture.For(destination));
        Assert.NotEmpty(PlaceFixture.For(destination));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Atlantis")]
    public void Unknown_destinations_fall_back_to_Tokyo(string? destination)
    {
        Assert.Same(FlightFixture.For("Tokyo"), FlightFixture.For(destination!));
        Assert.Same(HotelFixture.For("Tokyo"), HotelFixture.For(destination!));
        Assert.Same(PlaceFixture.For("Tokyo"), PlaceFixture.For(destination!));
    }

    [Fact]
    public void Catalogue_covers_three_demo_destinations()
    {
        Assert.Equal(3, FlightFixture.ByDestination.Count);
        Assert.Equal(3, HotelFixture.ByDestination.Count);
        Assert.Equal(3, PlaceFixture.ByDestination.Count);
    }
}
