using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightPriceCalendar;

public sealed class GetFlightPriceCalendarQueryHandler(IFlightPriceCalendarProvider flightPriceCalendarProvider, IUserPreferencesRepository userPreferencesRepository, ICurrentUserContext currentUserContext, ILogger<GetFlightPriceCalendarQueryHandler> logger) : IRequestHandler<GetFlightPriceCalendarQuery, Result<GetFlightPriceCalendarResponseDTO>>
{
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase) { "USD", "EUR", "UAH" };
    private const string DefaultCurrency = "USD";

    public async Task<Result<GetFlightPriceCalendarResponseDTO>> Handle(GetFlightPriceCalendarQuery request, CancellationToken cancellationToken)
    {
        var calendarRequest = request.Request;
        var currency = await ResolveCalendarCurrencyAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysInMonth = DateTime.DaysInMonth(calendarRequest.Year, calendarRequest.Month);
        var monthPrices = await GetProviderPricesAsync(calendarRequest, currency, cancellationToken);

        var days = new List<PriceCalendarDayDTO>(daysInMonth);

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(calendarRequest.Year, calendarRequest.Month, day);
            var dateString = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var price = date >= today && monthPrices.TryGetValue(date, out var dayPrice) ? dayPrice : null;

            days.Add(new PriceCalendarDayDTO { Date = dateString, LowestPrice = price, Currency = currency });
        }

        var cheapestDay = days.Where(d => d.LowestPrice.HasValue).MinBy(d => d.LowestPrice!.Value);

        return Result.Success(new GetFlightPriceCalendarResponseDTO
        {
            Days = days,
            CheapestDate = cheapestDay?.Date,
            CheapestPrice = cheapestDay?.LowestPrice
        });
    }

    private async Task<IReadOnlyDictionary<DateOnly, decimal?>> GetProviderPricesAsync(GetFlightPriceCalendarRequestDTO calendarRequest, string currency, CancellationToken cancellationToken)
    {
        var providerRequest = new FlightPriceCalendarProviderRequest(calendarRequest.DepartureId, calendarRequest.ArrivalId, calendarRequest.Year, calendarRequest.Month, currency, calendarRequest.TripLengthDays);
        var result = await flightPriceCalendarProvider.GetMonthlyPricesAsync(providerRequest, cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Flight price calendar provider failed for {Departure}->{Arrival} {Year}-{Month}: {ErrorCode}", calendarRequest.DepartureId, calendarRequest.ArrivalId, calendarRequest.Year, calendarRequest.Month, result.Error.Code);
            return new Dictionary<DateOnly, decimal?>();
        }

        // The provider already deduplicates per date; no GroupBy needed here
        return result.Value.Days
            .ToDictionary(day => day.Date, day => (decimal?)day.LowestPrice);
    }

    private async Task<string> ResolveCalendarCurrencyAsync(CancellationToken cancellationToken)
    {
        var userId = currentUserContext.GetRequiredUser().Id;
        var userPreferences = await userPreferencesRepository.GetByUserIdAsync(userId, cancellationToken);

        var currency = userPreferences
            .Where(preference => preference.IsActive
                && preference.PreferenceType.ServiceTypeId == ServiceType.Flight.Id
                && string.Equals(preference.PreferenceType.Name, PreferenceType.Currency.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(preference => preference.UpdatedAt)
            .Select(preference => ExtractPreferenceValue(preference.PreferencesJson))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (currency is null)
            logger.LogDebug("No active flight currency preference found for user {UserId}, defaulting to {Currency}", userId, DefaultCurrency);

        return NormalizeCurrency(currency);
    }

    private static string NormalizeCurrency(string? currency)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? DefaultCurrency
            : currency.Trim().ToUpperInvariant();

        return SupportedCurrencies.Contains(normalizedCurrency) ? normalizedCurrency : DefaultCurrency;
    }

    private static string? ExtractPreferenceValue(string preferencesJson)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(preferencesJson);
            var root = jsonDoc.RootElement;
            var valueElement = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var value) ? value : root;

            return valueElement.ValueKind switch
            {
                JsonValueKind.String => valueElement.GetString(),
                JsonValueKind.Number => valueElement.GetRawText(),
                _ => null
            };
        }
        catch (JsonException)
        {
            // Malformed JSON: return null and let the caller fall back to DefaultCurrency
            return null;
        }
    }
}
