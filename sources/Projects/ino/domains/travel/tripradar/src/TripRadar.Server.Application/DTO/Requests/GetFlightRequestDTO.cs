using System.Text.Json.Serialization;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetFlightRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    public required string Username { get; set; }

    public FlightSearchQueryDTO? FlightSearch { get; set; }

    [JsonPropertyName("localization")]
    public Localization? Localization { get; set; }

    public required AdvancedSearchOptions AdvancedOptions { get; set; }

    [JsonPropertyName("passengers")]
    public PassengerInfo? Passengers { get; set; }

    [JsonPropertyName("sorting")]
    public SortingOptions? Sorting { get; set; }

    [JsonPropertyName("filters")]
    public AdvancedFilters? Filters { get; set; }

    [JsonPropertyName("nextFlights")]
    public NextFlights? NextFlights { get; set; }

    [JsonPropertyName("booking")]
    public BookingFlights? Booking { get; set; }

    [Preference(nameof(PreferenceType.NoTraceMode))]
    [JsonPropertyName("zeroTrace")]
    public bool? ZeroTrace { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_flights", ht);

        if (AdvancedOptions.Type != FlightType.MultiCity && FlightSearch is not null)
        {
            AddIfNotNull("departure_id", FlightSearch.DepartureId, ht);
            AddIfNotNull("arrival_id", FlightSearch.ArrivalId, ht);
            AddIfNotNull("outbound_date", AdvancedOptions.OutboundDate, ht);
            AddIfNotNull("return_date", AdvancedOptions.ReturnDate, ht);
        }

        var type = AdvancedOptions.Type.HasValue ? ((int)AdvancedOptions.Type.Value).ToString(CultureInfo.InvariantCulture) : string.IsNullOrWhiteSpace(AdvancedOptions.ReturnDate) ? ((int)FlightType.OneWay).ToString(CultureInfo.InvariantCulture) : ((int)FlightType.RoundTrip).ToString(CultureInfo.InvariantCulture);
        AddIfNotNull("type", type, ht);
        AddIfNotNull("adults", Passengers?.Adults?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("children", Passengers?.Children?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("infants_in_seat", Passengers?.InfantsInSeat?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("infants_in_lap", Passengers?.InfantsOnLap?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("currency", Localization?.Currency, ht);
        AddIfNotNull("hl", Localization?.Hl, ht);
        AddIfNotNull("gl", Localization?.Gl, ht);
        AddIfNotNull("max_price", Filters?.MaxPrice?.ToString(CultureInfo.InvariantCulture), ht);
        if (Filters?.Stops is { } stops) AddIfNotNull("stops", ((int)stops).ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("include_airlines", string.IsNullOrWhiteSpace(Filters?.IncludeAirlines) ? null : Filters.IncludeAirlines, ht);
        AddIfNotNull("exclude_airlines", string.IsNullOrWhiteSpace(Filters?.ExcludeAirlines) ? null : Filters.ExcludeAirlines, ht);
        AddIfNotNull("bags", Filters?.Bags?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("emissions", Filters?.Emissions?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("max_duration", Filters?.MaxDuration?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("layover_duration", string.IsNullOrWhiteSpace(Filters?.LayoverDuration) ? null : Filters.LayoverDuration, ht);
        AddIfNotNull("outbound_times", string.IsNullOrWhiteSpace(Filters?.OutboundTimes) ? null : Filters.OutboundTimes, ht);
        AddIfNotNull("return_times", string.IsNullOrWhiteSpace(Filters?.ReturnTimes) ? null : Filters.ReturnTimes, ht);
        if (Sorting?.SortBy is { } sortBy) AddIfNotNull("sort_by", ((int)sortBy).ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("departure_token", NextFlights?.DepartureToken, ht);
        AddIfNotNull("booking_token", Booking?.BookingToken, ht);
        if (AdvancedOptions?.TravelClass is { } travelClass) AddIfNotNull("travel_class", ((int)travelClass).ToString(CultureInfo.InvariantCulture), ht);
        if (AdvancedOptions?.MultiCityJson is { Count: > 0 }) AddIfNotNull("multi_city_json", JsonSerializer.Serialize(AdvancedOptions.MultiCityJson), ht);
        if (AdvancedOptions?.ShowHidden is { } showHidden) AddIfNotNull("show_hidden", showHidden.ToString().ToLowerInvariant(), ht);
        if (AdvancedOptions?.DeepSearch is { } deepSearch) AddIfNotNull("deep_search", deepSearch.ToString().ToLowerInvariant(), ht);
        if (ZeroTrace == true) AddIfNotNull("zero_trace", "true", ht);

        return ht;
    }
}
