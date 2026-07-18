using System.Collections;
using System.Globalization;
using TripRadar.Server.Application.Contracts.Requests;

namespace TripRadar.Server.Application.DTO.Requests;

/// <summary>
/// Application DTO for Google Travel Explore API request.
/// Implements ISerpApiRequest to build query parameters for SerpApi.
/// </summary>
public class GetFlightExploreRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    public required string Username { get; set; }

    #region Search Query

    /// <summary>
    /// Departure airport code or city kgmid (required).
    /// </summary>
    public required string DepartureId { get; set; }

    #endregion

    #region Localization

    public string? Gl { get; set; }
    public string? Hl { get; set; }
    public string? Currency { get; set; }

    #endregion

    #region Advanced Google Travel Explore Parameters

    public string? ArrivalAreaId { get; set; }
    public string? ArrivalId { get; set; }
    public int? Type { get; set; }
    public string? OutboundDate { get; set; }
    public string? ReturnDate { get; set; }
    public int? Month { get; set; }
    public int? TravelDuration { get; set; }
    public int? TravelClass { get; set; }

    #endregion

    #region Number of Passengers

    public int? Adults { get; set; }
    public int? Children { get; set; }
    public int? InfantsInSeat { get; set; }
    public int? InfantsOnLap { get; set; }

    #endregion

    #region Advanced Filters

    public int? Stops { get; set; }
    public int? TravelMode { get; set; }
    public string? Interest { get; set; }
    public string? IncludeAirlines { get; set; }
    public int? Bags { get; set; }
    public int? MaxPrice { get; set; }
    public int? MaxDuration { get; set; }

    #endregion

    #region SerpApi Parameters

    public bool? NoCache { get; set; }
    public bool? Async { get; set; }
    public bool? ZeroTrace { get; set; }
    public string? Output { get; set; }
    public string? JsonRestrictor { get; set; }

    #endregion

    /// <summary>
    /// Builds the Hashtable of query parameters for SerpApi.
    /// Only includes parameters that have values.
    /// </summary>
    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        // Engine is always google_travel_explore
        AddIfNotNull("engine", "google_travel_explore", ht);

        // Search Query
        AddIfNotNull("departure_id", DepartureId, ht);

        // Localization
        AddIfNotNull("gl", Gl, ht);
        AddIfNotNull("hl", Hl, ht);
        AddIfNotNull("currency", Currency, ht);

        // Advanced Google Travel Explore Parameters
        AddIfNotNull("arrival_area_id", ArrivalAreaId, ht);
        AddIfNotNull("arrival_id", ArrivalId, ht);
        AddIfNotNull("type", Type?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("outbound_date", OutboundDate, ht);
        AddIfNotNull("return_date", ReturnDate, ht);
        AddIfNotNull("month", Month?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("travel_duration", TravelDuration?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("travel_class", TravelClass?.ToString(CultureInfo.InvariantCulture), ht);

        // Number of Passengers
        AddIfNotNull("adults", Adults?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("children", Children?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("infants_in_seat", InfantsInSeat?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("infants_on_lap", InfantsOnLap?.ToString(CultureInfo.InvariantCulture), ht);

        // Advanced Filters
        AddIfNotNull("stops", Stops?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("travel_mode", TravelMode?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("interest", Interest, ht);
        AddIfNotNull("include_airlines", IncludeAirlines, ht);
        AddIfNotNull("bags", Bags?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("max_price", MaxPrice?.ToString(CultureInfo.InvariantCulture), ht);
        AddIfNotNull("max_duration", MaxDuration?.ToString(CultureInfo.InvariantCulture), ht);

        // SerpApi Parameters
        if (NoCache == true) AddIfNotNull("no_cache", "true", ht);
        if (Async == true) AddIfNotNull("async", "true", ht);
        if (ZeroTrace == true) AddIfNotNull("zero_trace", "true", ht);
        AddIfNotNull("output", Output, ht);
        AddIfNotNull("json_restrictor", JsonRestrictor, ht);

        return ht;
    }
}
