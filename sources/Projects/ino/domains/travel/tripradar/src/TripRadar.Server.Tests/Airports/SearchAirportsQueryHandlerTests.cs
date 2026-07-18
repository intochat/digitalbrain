using Moq;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.Airports.Queries.SearchAirports;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Tests.Airports;

public class SearchAirportsQueryHandlerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAirportRepository> _airportRepo = new();
    private readonly Mock<ICityTranslationProvider> _cityTranslation = new();
    private readonly SearchAirportsQueryHandler _handler;

    public SearchAirportsQueryHandlerTests()
    {
        _unitOfWork.Setup(u => u.AirportRepository).Returns(_airportRepo.Object);
        _handler = new SearchAirportsQueryHandler(_unitOfWork.Object, _cityTranslation.Object);
    }

    [Fact]
    public async Task Handle_ReturnsRawEntityFieldsAndIsoCountryCode()
    {
        var airports = CreateAirports(("BCN", "Josep Tarradellas Barcelona-El Prat Airport", "Barcelona", "es", 41.297, 2.078, "large_airport"));

        _airportRepo.Setup(r => r.SearchAsync("bar", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(airports);

        var result = await _handler.Handle(new SearchAirportsQuery("bar"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var suggestion = result.Value.First();
        suggestion.Code.Should().Be("BCN");
        suggestion.Name.Should().Be("Josep Tarradellas Barcelona-El Prat Airport");
        suggestion.City.Should().Be("Barcelona");
        suggestion.CountryCode.Should().Be("ES");
    }

    [Fact]
    public async Task Handle_DoesNotApplyTitleCaseOrCityOverrides()
    {
        var airports = CreateAirports(("BVA", "paris-beauvais airport", "tillé", "fr", 49.45, 2.11, "large_airport"));

        _airportRepo.Setup(r => r.SearchAsync("paris", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(airports);

        var result = await _handler.Handle(new SearchAirportsQuery("paris"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var suggestion = result.Value.First();
        suggestion.Name.Should().Be("paris-beauvais airport");
        suggestion.City.Should().Be("tillé");
        suggestion.CountryCode.Should().Be("FR");
    }

    [Fact]
    public async Task Handle_ReturnsEmptyListForBlankQuery()
    {
        var result = await _handler.Handle(new SearchAirportsQuery("  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CyrillicQuery_FallsBackToEnglishTranslation_WhenHlProvided()
    {
        var moscowAirports = CreateAirports(
            ("SVO", "sheremetyevo international airport", "moscow", "ru", 55.97, 37.41, "large_airport"),
            ("DME", "domodedovo international airport", "moscow", "ru", 55.41, 37.90, "large_airport"));

        _airportRepo.Setup(r => r.SearchAsync("Москва", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Airport>());

        _airportRepo.Setup(r => r.SearchAsync("Moscow", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(moscowAirports);

        _cityTranslation.Setup(t => t.GetEnglishCityName("Москва"))
            .Returns("Moscow");

        var result = await _handler.Handle(new SearchAirportsQuery("Москва", 10, "ru"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(a => a.Code == "SVO");
        result.Value.Should().Contain(a => a.Code == "DME");
    }

    [Fact]
    public async Task Handle_CyrillicQuery_DoesNotFallBack_WhenHlNotProvided()
    {
        _airportRepo.Setup(r => r.SearchAsync("Москва", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Airport>());

        var result = await _handler.Handle(new SearchAirportsQuery("Москва", 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _cityTranslation.Verify(t => t.GetEnglishCityName(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CyrillicQuery_SkipsFallback_WhenDirectSearchReturnsResults()
    {
        var airports = CreateAirports(("SVO", "sheremetyevo", "moscow", "ru", 55.97, 37.41, "large_airport"));

        _airportRepo.Setup(r => r.SearchAsync("Шереметьево", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(airports);

        var result = await _handler.Handle(new SearchAirportsQuery("Шереметьево", 10, "ru"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        _cityTranslation.Verify(t => t.GetEnglishCityName(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CyrillicQuery_ReturnsEmpty_WhenTranslationNotFound()
    {
        _airportRepo.Setup(r => r.SearchAsync("НеизвестныйГород", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Airport>());

        _cityTranslation.Setup(t => t.GetEnglishCityName("НеизвестныйГород"))
            .Returns((string?)null);

        var result = await _handler.Handle(new SearchAirportsQuery("НеизвестныйГород", 10, "ru"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesQueryAndLimitToRepository()
    {
        _airportRepo.Setup(r => r.SearchAsync("москва", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .Verifiable();

        var result = await _handler.Handle(new SearchAirportsQuery("москва", 5, "ru"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _airportRepo.Verify();
    }

    private static IReadOnlyList<Airport> CreateAirports(params (string Code, string Name, string City, string Country, double? Lat, double? Lng, string? Type)[] data)
    {
        return data.Select(d =>
        {
            var airport = (Airport)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Airport));
            typeof(Airport).GetProperty("Code")!.GetSetMethod(true)!.Invoke(airport, [d.Code]);
            typeof(Airport).GetProperty("Name")!.GetSetMethod(true)!.Invoke(airport, [d.Name]);
            typeof(Airport).GetProperty("City")!.GetSetMethod(true)!.Invoke(airport, [d.City]);
            typeof(Airport).GetProperty("Country")!.GetSetMethod(true)!.Invoke(airport, [d.Country]);
            typeof(Airport).GetProperty("Latitude")!.GetSetMethod(true)!.Invoke(airport, [d.Lat]);
            typeof(Airport).GetProperty("Longitude")!.GetSetMethod(true)!.Invoke(airport, [d.Lng]);
            typeof(Airport).GetProperty("AirportType")!.GetSetMethod(true)!.Invoke(airport, [d.Type]);
            return airport;
        }).ToList();
    }
}
