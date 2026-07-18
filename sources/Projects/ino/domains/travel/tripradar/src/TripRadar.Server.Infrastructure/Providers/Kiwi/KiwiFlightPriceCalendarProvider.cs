using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Providers.Kiwi.Models;
using TripRadar.Server.Infrastructure.Providers.Kiwi.Settings;

namespace TripRadar.Server.Infrastructure.Providers.Kiwi;

public sealed partial class KiwiFlightPriceCalendarProvider : IFlightPriceCalendarProvider
{
    private const string CalendarQuery = """
        query CalendarPricesFetcherQuery(
          $search: SearchPricesCalendarInput
          $filter: ItinerariesFilterInput
          $options: ItinerariesOptionsInput
        ) {
          itineraryPricesCalendar(search: $search, filter: $filter, options: $options) {
            __typename
            ... on ItineraryPricesCalendar {
              calendar {
                date
                ratedPrice {
                  price {
                    amount
                  }
                  rating
                }
              }
            }
            ... on AppError {
              error: message
            }
          }
        }
        """;

    private readonly HttpClient _httpClient;
    private readonly KiwiCalendarSettings _settings;
    private readonly ILogger<KiwiFlightPriceCalendarProvider> _logger;
    private readonly string _calendarUri;

    public KiwiFlightPriceCalendarProvider(
        HttpClient httpClient,
        IOptions<KiwiCalendarSettings> options,
        ILogger<KiwiFlightPriceCalendarProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = options.Value;
        // Cache the URI once; BaseUrl and endpoint are immutable after startup
        _calendarUri = $"{_settings.BaseUrl.TrimEnd('/')}{KiwiConstants.CalendarPricesEndpoint}";
    }

    public async Task<Result<FlightPriceCalendarProviderResponse>> GetMonthlyPricesAsync(FlightPriceCalendarProviderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var fetchResult = await FetchCalendarAsync(request, cancellationToken);
            if (fetchResult.IsFailure)
                return Result.Failure<FlightPriceCalendarProviderResponse>(fetchResult.Error);

            var calendar = fetchResult.Value?.Data?.ItineraryPricesCalendar;

            if (!string.IsNullOrWhiteSpace(calendar?.Error))
            {
                LogCalendarError(_logger, calendar.Error, request.DepartureId, request.ArrivalId, request.Year, request.Month);
                return Result.Failure<FlightPriceCalendarProviderResponse>(Errors.KiwiCalendarRequestFailed);
            }

            // A null calendar with no error means the upstream returned an unexpected shape
            if (calendar?.Calendar is null)
            {
                LogCalendarEmpty(_logger, request.DepartureId, request.ArrivalId, request.Year, request.Month);
                return Result.Success(new FlightPriceCalendarProviderResponse([]));
            }

            var currencyUpper = request.Currency.ToUpperInvariant();
            var days = MapPricesToDays(calendar.Calendar, request.Year, request.Month, currencyUpper).ToList();
            return Result.Success(new FlightPriceCalendarProviderResponse(days));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogCalendarException(_logger, request.DepartureId, request.ArrivalId, request.Year, request.Month, exception);
            return Result.Failure<FlightPriceCalendarProviderResponse>(Errors.KiwiCalendarRequestFailed);
        }
    }

    private async Task<Result<KiwiPricePerDateResponse?>> FetchCalendarAsync(FlightPriceCalendarProviderRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = CreateHttpRequest(request);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogCalendarRequestFailed(_logger, response.StatusCode, request.DepartureId, request.ArrivalId, request.Year, request.Month);
            return Result.Failure<KiwiPricePerDateResponse?>(Errors.KiwiCalendarRequestFailed);
        }

        var payload = await response.Content.ReadFromJsonAsync<KiwiPricePerDateResponse>(cancellationToken);
        return Result.Success(payload);
    }

    private HttpRequestMessage CreateHttpRequest(FlightPriceCalendarProviderRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, _calendarUri);
        message.Headers.TryAddWithoutValidation("Accept", "application/json");
        message.Headers.TryAddWithoutValidation("User-Agent", KiwiConstants.UserAgent);
        message.Content = JsonContent.Create(BuildPayload(request));
        return message;
    }

    private object BuildPayload(FlightPriceCalendarProviderRequest request)
    {
        var monthStart = new DateOnly(request.Year, request.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        return new
        {
            query = CalendarQuery,
            variables = new
            {
                search = BuildSearch(request, monthStart, monthEnd),
                filter = BuildFilter(),
                options = BuildOptions(request)
            }
        };
    }

    private static object BuildSearch(FlightPriceCalendarProviderRequest request, DateOnly monthStart, DateOnly monthEnd) => new
    {
        source = new { ids = new[] { request.DepartureId.Trim() } },
        destination = new { ids = new[] { request.ArrivalId.Trim() } },
        dates = new
        {
            start = ToKiwiDateTime(monthStart, "00:00:00"),
            end = ToKiwiDateTime(monthEnd, "23:59:59")
        },
        passengers = new
        {
            adults = 1,
            children = 0,
            infants = 0,
            adultsHoldBags = new[] { 0 },
            adultsHandBags = new[] { 0 },
            childrenHoldBags = Array.Empty<int>(),
            childrenHandBags = Array.Empty<int>()
        },
        cabinClass = new
        {
            cabinClass = "ECONOMY",
            applyMixedClasses = false
        }
    };

    private static object BuildFilter() => new
    {
        allowChangeInboundDestination = true,
        allowChangeInboundSource = true,
        allowDifferentStationConnection = true,
        enableSelfTransfer = true,
        enableThrowAwayTicketing = true,
        enableTrueHiddenCity = true,
        transportTypes = new[] { "FLIGHT" },
        contentProviders = new[] { "KIWI" },
        flightsApiLimit = 25
    };

    private object BuildOptions(FlightPriceCalendarProviderRequest request)
    {
        // Currency is lowercase for the Kiwi API request; responses use uppercase.
        var currency = request.Currency.ToLowerInvariant();
        var market = ResolveMarket(_settings.Market);
        var partner = ResolvePartner(_settings.Partner);

        return new
        {
            sortBy = "QUALITY",
            mergePriceDiffRule = "INCREASED",
            contentProviders = new[] { "KIWI" },
            currency,
            apiUrl = (string?)null,
            locale = ResolveLocale(_settings.DefaultLocale),
            market,
            partner,
            partnerMarket = market,
            affilID = partner,
            storeSearch = false,
            searchStrategy = "REDUCED",
            kiwiClub = new
            {
                isKiwiClubMember = false,
                isPhoneVerified = false,
                kiwiClubPerksEligible = false
            },
            abTestInput = new
            {
                profitabilityFixedCpa = "DISABLE",
                onePerDayCharybdis = "ENABLE"
            }
        };
    }

    /// <summary>
    /// Maps raw Kiwi price items to calendar days, deduplicating per date by lowest price.
    /// Currency case asymmetry is intentional: the API receives lowercase, responses use uppercase.
    /// </summary>
    private static IEnumerable<FlightPriceCalendarProviderDay> MapPricesToDays(IReadOnlyCollection<KiwiPricePerDateItem> prices, int year, int month, string currencyUpper) =>
        prices
            .Where(IsValidPrice)
            .Select(p => (Date: ParseDate(p.Date), Item: p))
            .Where(x => x.Date is { Year: var y, Month: var m } && y == year && m == month)
            .Select(x => new FlightPriceCalendarProviderDay(
                x.Date!.Value,
                x.Item.RatedPrice!.Price!.Amount!.Value,
                currencyUpper))
            .GroupBy(day => day.Date)
            .Select(group => group.MinBy(day => day.LowestPrice)!)
            .OrderBy(day => day.Date);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kiwi calendar request failed with {StatusCode} for {Departure}->{Arrival} {Year}-{Month}")]
    private static partial void LogCalendarRequestFailed(ILogger logger, System.Net.HttpStatusCode statusCode, string departure, string arrival, int year, int month);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kiwi calendar returned error '{Error}' for {Departure}->{Arrival} {Year}-{Month}")]
    private static partial void LogCalendarError(ILogger logger, string error, string departure, string arrival, int year, int month);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kiwi calendar returned null data (no error) for {Departure}->{Arrival} {Year}-{Month}")]
    private static partial void LogCalendarEmpty(ILogger logger, string departure, string arrival, int year, int month);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kiwi calendar request threw an exception for {Departure}->{Arrival} {Year}-{Month}")]
    private static partial void LogCalendarException(ILogger logger, string departure, string arrival, int year, int month, Exception exception);

    private static string ResolveLocale(string? locale) => string.IsNullOrWhiteSpace(locale) ? KiwiConstants.DefaultLocale : locale.Trim().ToLowerInvariant();

    private static string ResolveMarket(string? market) => string.IsNullOrWhiteSpace(market) ? KiwiConstants.DefaultMarket : market.Trim().ToLowerInvariant();

    private static string ResolvePartner(string? partner) => string.IsNullOrWhiteSpace(partner) ? KiwiConstants.DefaultPartner : partner.Trim().ToLowerInvariant();

    private static string ToKiwiDateTime(DateOnly date, string time) => $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}T{time}";

    private static bool IsValidPrice(KiwiPricePerDateItem price) => price.RatedPrice?.Price?.Amount is > 0;

    private static bool TryParseDate(string? value, out DateOnly date) => DateOnly.TryParseExact(value, KiwiConstants.ResponseDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateOnly? ParseDate(string? value) => TryParseDate(value, out var date) ? date : null;
}
