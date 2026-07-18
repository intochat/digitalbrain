using Moq;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.UseCases.Locations.Queries.SearchLocations;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Tests.Locations;

public class SearchLocationsQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILocationRepository> _locationRepository = new();
    private readonly SearchLocationsQueryHandler _handler;

    public SearchLocationsQueryHandlerTests()
    {
        _unitOfWork.Setup(u => u.LocationRepository).Returns(_locationRepository.Object);
        _handler = new SearchLocationsQueryHandler(_unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyListForBlankQuery()
    {
        var result = await _handler.Handle(new SearchLocationsQuery("  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _locationRepository.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MapsLocationSuggestions()
    {
        var locations = CreateLocations((
            LocationId: 42,
            Name: "Barcelona",
            CanonicalName: "Barcelona, Catalonia, Spain",
            CountryCode: "es",
            TargetType: "CITY",
            Reach: 1000,
            Latitude: 41.3874,
            Longitude: 2.1686));

        _locationRepository.Setup(r => r.SearchAsync("bar", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locations);

        var result = await _handler.Handle(new SearchLocationsQuery("bar"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();

        var suggestion = result.Value.Single();
        suggestion.LocationId.Should().Be(42);
        suggestion.Name.Should().Be("Barcelona");
        suggestion.CanonicalName.Should().Be("Barcelona, Catalonia, Spain");
        suggestion.CountryCode.Should().Be("ES");
        suggestion.TargetType.Should().Be("CITY");
        suggestion.GpsLatitude.Should().Be(41.3874);
        suggestion.GpsLongitude.Should().Be(2.1686);
    }

    private static IReadOnlyList<Location> CreateLocations(params (int LocationId, string Name, string CanonicalName, string CountryCode, string TargetType, int? Reach, double? Latitude, double? Longitude)[] data)
    {
        return data.Select(item =>
        {
            var location = (Location)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Location));
            typeof(Location).GetProperty(nameof(Location.LocationId))!.GetSetMethod(true)!.Invoke(location, [item.LocationId]);
            typeof(Location).GetProperty(nameof(Location.Name))!.GetSetMethod(true)!.Invoke(location, [item.Name]);
            typeof(Location).GetProperty(nameof(Location.CanonicalName))!.GetSetMethod(true)!.Invoke(location, [item.CanonicalName]);
            typeof(Location).GetProperty(nameof(Location.CountryCode))!.GetSetMethod(true)!.Invoke(location, [item.CountryCode]);
            typeof(Location).GetProperty(nameof(Location.TargetType))!.GetSetMethod(true)!.Invoke(location, [item.TargetType]);
            typeof(Location).GetProperty(nameof(Location.Reach))!.GetSetMethod(true)!.Invoke(location, [item.Reach]);
            typeof(Location).GetProperty(nameof(Location.GpsLatitude))!.GetSetMethod(true)!.Invoke(location, [item.Latitude]);
            typeof(Location).GetProperty(nameof(Location.GpsLongitude))!.GetSetMethod(true)!.Invoke(location, [item.Longitude]);
            return location;
        }).ToList();
    }
}
