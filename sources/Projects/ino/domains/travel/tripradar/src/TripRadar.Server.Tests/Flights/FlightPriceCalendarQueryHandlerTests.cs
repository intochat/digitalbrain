using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightPriceCalendar;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using ServiceType = TripRadar.Server.Domain.Enums.ServiceType;
using PreferenceType = TripRadar.Server.Domain.Enums.PreferenceType;

namespace TripRadar.Server.Tests.Flights;

public class FlightPriceCalendarQueryHandlerTests
{
    private readonly Mock<IFlightPriceCalendarProvider> _provider = new();
    private readonly Mock<IUserPreferencesRepository> _userPreferencesRepository = new();
    private readonly Mock<ICurrentUserContext> _currentUserContext = new();
    private readonly User _user = User.Register("P@ssw0rd!123", "calendar@example.com", true);

    public FlightPriceCalendarQueryHandlerTests()
    {
        _currentUserContext.Setup(context => context.GetRequiredUser()).Returns(_user);
        _userPreferencesRepository.Setup(repository => repository.GetByUserIdAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task PriceCalendar_UsesProviderMonthPricesAndSelectsCheapestDate()
    {
        var month = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);
        var providerDays = new[]
        {
            new FlightPriceCalendarProviderDay(new DateOnly(month.Year, month.Month, 5), 240m, "USD"),
            new FlightPriceCalendarProviderDay(new DateOnly(month.Year, month.Month, 6), 180m, "USD")
        };

        _provider.Setup(p => p.GetMonthlyPricesAsync(
                It.Is<FlightPriceCalendarProviderRequest>(r => r.DepartureId == "SOF" && r.ArrivalId == "BCN" && r.Year == month.Year && r.Month == month.Month),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FlightPriceCalendarProviderResponse(providerDays)));

        var handler = CreateHandler();
        var request = new GetFlightPriceCalendarRequestDTO
        {
            DepartureId = "SOF",
            ArrivalId = "BCN",
            Year = month.Year,
            Month = month.Month,
            Currency = "USD"
        };

        var result = await handler.Handle(new GetFlightPriceCalendarQuery(request, "user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Days.Should().HaveCount(DateTime.DaysInMonth(month.Year, month.Month));
        result.Value.Days.Single(day => day.Date == new DateOnly(month.Year, month.Month, 5).ToString("yyyy-MM-dd")).LowestPrice.Should().Be(240m);
        result.Value.Days.Single(day => day.Date == new DateOnly(month.Year, month.Month, 6).ToString("yyyy-MM-dd")).LowestPrice.Should().Be(180m);
        result.Value!.CheapestDate.Should().Be(new DateOnly(month.Year, month.Month, 6).ToString("yyyy-MM-dd"));
        result.Value!.CheapestPrice.Should().Be(180m);
    }

    [Fact]
    public async Task PriceCalendar_ReturnsEmptyPricesWhenProviderFails()
    {
        var month = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);
        _provider.Setup(p => p.GetMonthlyPricesAsync(It.IsAny<FlightPriceCalendarProviderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<FlightPriceCalendarProviderResponse>(Errors.KiwiCalendarRequestFailed));

        var handler = CreateHandler();
        var request = new GetFlightPriceCalendarRequestDTO
        {
            DepartureId = "SOF",
            ArrivalId = "BCN",
            Year = month.Year,
            Month = month.Month,
            Currency = "USD"
        };

        var result = await handler.Handle(new GetFlightPriceCalendarQuery(request, "user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Days.Should().OnlyContain(day => !day.LowestPrice.HasValue);
        result.Value!.CheapestDate.Should().BeNull();
        result.Value!.CheapestPrice.Should().BeNull();
    }

    [Fact]
    public async Task PriceCalendar_NormalizesUnsupportedCurrencyBeforeCallingKiwi()
    {
        var month = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);
        FlightPriceCalendarProviderRequest? capturedRequest = null;

        _provider.Setup(p => p.GetMonthlyPricesAsync(It.IsAny<FlightPriceCalendarProviderRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FlightPriceCalendarProviderRequest, CancellationToken>((providerRequest, _) => capturedRequest = providerRequest)
            .ReturnsAsync(Result.Success(new FlightPriceCalendarProviderResponse([])));

        var handler = CreateHandler();
        var request = new GetFlightPriceCalendarRequestDTO
        {
            DepartureId = "SOF",
            ArrivalId = "VIE",
            Year = month.Year,
            Month = month.Month,
            Currency = "RUB"
        };

        var result = await handler.Handle(new GetFlightPriceCalendarQuery(request, "user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Currency.Should().Be("USD");
        result.Value!.Days.Should().OnlyContain(day => day.Currency == "USD");
    }

    [Theory]
    [InlineData("EUR", "EUR", 99)]
    [InlineData("UAH", "UAH", 6251)]
    public async Task PriceCalendar_UsesSupportedUserFlightCurrencyPreferenceBeforeRequestCurrency(string preferenceCurrency, string expectedCurrency, decimal price)
    {
        var month = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);
        FlightPriceCalendarProviderRequest? capturedRequest = null;
        _userPreferencesRepository.Setup(repository => repository.GetByUserIdAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateFlightCurrencyPreference($$"""{ "value": "{{preferenceCurrency}}" }""")]);

        _provider.Setup(p => p.GetMonthlyPricesAsync(It.IsAny<FlightPriceCalendarProviderRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FlightPriceCalendarProviderRequest, CancellationToken>((providerRequest, _) => capturedRequest = providerRequest)
            .ReturnsAsync(Result.Success(new FlightPriceCalendarProviderResponse(
            [
                new FlightPriceCalendarProviderDay(new DateOnly(month.Year, month.Month, 8), price, expectedCurrency)
            ])));

        var handler = CreateHandler();
        var request = new GetFlightPriceCalendarRequestDTO
        {
            DepartureId = "SOF",
            ArrivalId = "VIE",
            Year = month.Year,
            Month = month.Month,
            Currency = "USD"
        };

        var result = await handler.Handle(new GetFlightPriceCalendarQuery(request, "user"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Currency.Should().Be(expectedCurrency);
        result.Value!.Days.Single(day => day.LowestPrice == price).Currency.Should().Be(expectedCurrency);
    }

    private GetFlightPriceCalendarQueryHandler CreateHandler() =>
        new(
            _provider.Object,
            _userPreferencesRepository.Object,
            _currentUserContext.Object,
            NullLogger<GetFlightPriceCalendarQueryHandler>.Instance);

    private static UserPreference CreateFlightCurrencyPreference(string preferencesJson)
    {
        var preference = new UserPreference(0, PreferenceType.Currency.Id, preferencesJson);
        var preferenceType = (TripRadar.Server.Domain.Entities.PreferenceType)Activator.CreateInstance(
            typeof(TripRadar.Server.Domain.Entities.PreferenceType),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [],
            culture: null)!;

        SetProperty(preferenceType, nameof(TripRadar.Server.Domain.Entities.PreferenceType.ServiceTypeId), ServiceType.Flight.Id);
        SetProperty(preferenceType, nameof(TripRadar.Server.Domain.Entities.PreferenceType.Name), PreferenceType.Currency.Name);
        SetProperty(preference, nameof(UserPreference.PreferenceType), preferenceType);
        return preference;
    }

    private static void SetProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(target, value);
    }

}
