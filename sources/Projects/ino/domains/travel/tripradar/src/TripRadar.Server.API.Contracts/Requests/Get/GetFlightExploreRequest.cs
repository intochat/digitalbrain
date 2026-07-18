using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Get;

/// <summary>
/// Request for Google Travel Explore API (SerpApi google_travel_explore engine).
/// Mirrors SerpApi parameters exactly with snake_case JSON property names.
/// </summary>
public class GetFlightExploreRequest : IValidatableObject
{

    #region Search Query

    /// <summary>
    /// Departure airport code or city location kgmid (e.g., "JFK", "/m/02_286").
    /// Multiple departure airports separated by comma are allowed (e.g., "CDG,ORY,/m/04jpl").
    /// </summary>
    [JsonPropertyName("departure_id")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Departure ID is required.")]
    public required string DepartureId { get; set; }

    #endregion

    #region Localization

    /// <summary>
    /// Country code for Google Travel search (e.g., "us", "uk", "fr").
    /// </summary>
    [JsonPropertyName("gl")]
    public string? Gl { get; set; }

    /// <summary>
    /// Language code for Google Travel search (e.g., "en", "es", "fr").
    /// </summary>
    [JsonPropertyName("hl")]
    public string? Hl { get; set; }

    /// <summary>
    /// Currency code for returned prices (default: USD).
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    #endregion

    #region Advanced Google Travel Explore Parameters

    /// <summary>
    /// Arrival region or country as a location kgmid (e.g., "/m/02j9z" for Europe).
    /// Mutually exclusive with ArrivalId.
    /// </summary>
    [JsonPropertyName("arrival_area_id")]
    public string? ArrivalAreaId { get; set; }

    /// <summary>
    /// Arrival airport code or city location kgmid (e.g., "LAX", "/m/0vzm").
    /// Mutually exclusive with ArrivalAreaId.
    /// </summary>
    [JsonPropertyName("arrival_id")]
    public string? ArrivalId { get; set; }

    /// <summary>
    /// Flight type: 1 = Round trip (default), 2 = One way.
    /// </summary>
    [JsonPropertyName("type")]
    public int? Type { get; set; }

    /// <summary>
    /// Outbound date in YYYY-MM-DD format.
    /// </summary>
    [JsonPropertyName("outbound_date")]
    public string? OutboundDate { get; set; }

    /// <summary>
    /// Return date in YYYY-MM-DD format. Required if type = 1 (Round trip).
    /// </summary>
    [JsonPropertyName("return_date")]
    public string? ReturnDate { get; set; }

    /// <summary>
    /// Month of trip with flexible travel dates (1-12, 0 = all months within next 6 months).
    /// </summary>
    [JsonPropertyName("month")]
    public int? Month { get; set; }

    /// <summary>
    /// Duration of trip with flexible dates: 1 = Weekend, 2 = 1 week (default), 3 = 2 weeks.
    /// </summary>
    [JsonPropertyName("travel_duration")]
    public int? TravelDuration { get; set; }

    /// <summary>
    /// Travel class: 1 = Economy (default), 2 = Premium economy, 3 = Business, 4 = First.
    /// </summary>
    [JsonPropertyName("travel_class")]
    public int? TravelClass { get; set; }

    #endregion

    #region Number of Passengers

    /// <summary>
    /// Number of adult passengers (default: 1).
    /// </summary>
    [JsonPropertyName("adults")]
    public int? Adults { get; set; }

    /// <summary>
    /// Number of child passengers (default: 0).
    /// </summary>
    [JsonPropertyName("children")]
    public int? Children { get; set; }

    /// <summary>
    /// Number of infants in seat (default: 0).
    /// </summary>
    [JsonPropertyName("infants_in_seat")]
    public int? InfantsInSeat { get; set; }

    /// <summary>
    /// Number of infants on lap (default: 0).
    /// </summary>
    [JsonPropertyName("infants_on_lap")]
    public int? InfantsOnLap { get; set; }

    #endregion

    #region Advanced Filters

    /// <summary>
    /// Number of stops: 0 = Any (default), 1 = Nonstop only, 2 = 1 stop or fewer, 3 = 2 stops or fewer.
    /// </summary>
    [JsonPropertyName("stops")]
    public int? Stops { get; set; }

    /// <summary>
    /// Travel mode: 0 = All (default), 1 = Flight only.
    /// Mutually exclusive with Interest.
    /// </summary>
    [JsonPropertyName("travel_mode")]
    public int? TravelMode { get; set; }

    /// <summary>
    /// Interest of destination (e.g., "0" = Popular, "/g/11bc58l13w" = Outdoors, "/m/0b3yr" = Beaches).
    /// Mutually exclusive with TravelMode.
    /// </summary>
    [JsonPropertyName("interest")]
    public string? Interest { get; set; }

    /// <summary>
    /// Airline codes to include, comma-separated (e.g., "UA,AA" or "STAR_ALLIANCE,SKYTEAM,ONEWORLD").
    /// </summary>
    [JsonPropertyName("include_airlines")]
    public string? IncludeAirlines { get; set; }

    /// <summary>
    /// Number of carry-on bags (default: 0). Should not exceed passengers with bag allowance.
    /// </summary>
    [JsonPropertyName("bags")]
    public int? Bags { get; set; }

    /// <summary>
    /// Maximum ticket price (default: unlimited).
    /// </summary>
    [JsonPropertyName("max_price")]
    public int? MaxPrice { get; set; }

    /// <summary>
    /// Maximum flight duration in minutes (e.g., 1500 for 25 hours).
    /// </summary>
    [JsonPropertyName("max_duration")]
    public int? MaxDuration { get; set; }

    #endregion

    #region SerpApi Parameters

    /// <summary>
    /// Force SerpApi to fetch fresh results (bypass cache).
    /// </summary>
    [JsonPropertyName("no_cache")]
    public bool? NoCache { get; set; }

    /// <summary>
    /// Async mode for SerpApi. Use Search Archive API to retrieve results later.
    /// </summary>
    [JsonPropertyName("async")]
    public bool? Async { get; set; }

    /// <summary>
    /// Enterprise only. ZeroTrace mode to skip storing search parameters on SerpApi servers.
    /// </summary>
    [JsonPropertyName("zero_trace")]
    public bool? ZeroTrace { get; set; }

    /// <summary>
    /// Output format: "json" (default) or "html".
    /// </summary>
    [JsonPropertyName("output")]
    public string? Output { get; set; }

    /// <summary>
    /// JSON restrictor to filter returned fields.
    /// </summary>
    [JsonPropertyName("json_restrictor")]
    public string? JsonRestrictor { get; set; }

    #endregion

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // departure_id is required
        if (string.IsNullOrWhiteSpace(DepartureId))
        {
            yield return new ValidationResult("Departure ID is required.", [nameof(DepartureId)]);
        }

        // arrival_id and arrival_area_id are mutually exclusive
        if (!string.IsNullOrWhiteSpace(ArrivalId) && !string.IsNullOrWhiteSpace(ArrivalAreaId))
        {
            yield return new ValidationResult("ArrivalId and ArrivalAreaId are mutually exclusive.", [nameof(ArrivalId), nameof(ArrivalAreaId)]);
        }

        // travel_mode and interest are mutually exclusive
        if (TravelMode.HasValue && TravelMode.Value != 0 && !string.IsNullOrWhiteSpace(Interest) && Interest != "0")
        {
            yield return new ValidationResult("TravelMode and Interest are mutually exclusive.", [nameof(TravelMode), nameof(Interest)]);
        }

        // kgmid values validation (basic check for /m/ prefix)
        if (!string.IsNullOrWhiteSpace(ArrivalAreaId) && !ArrivalAreaId.StartsWith("/m/", StringComparison.OrdinalIgnoreCase) && !ArrivalAreaId.StartsWith("/g/", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("ArrivalAreaId must be a valid kgmid starting with /m/ or /g/.", [nameof(ArrivalAreaId)]);
        }

        // Passenger counts sanity checks
        var adults = Adults ?? 1;
        var children = Children ?? 0;
        var infantsInSeat = InfantsInSeat ?? 0;
        var infantsOnLap = InfantsOnLap ?? 0;
        var totalPassengers = adults + children + infantsInSeat + infantsOnLap;

        if (adults < 0 || children < 0 || infantsInSeat < 0 || infantsOnLap < 0)
        {
            yield return new ValidationResult("Passenger counts cannot be negative.", [nameof(Adults), nameof(Children), nameof(InfantsInSeat), nameof(InfantsOnLap)]);
        }

        if (totalPassengers > 9)
        {
            yield return new ValidationResult("Maximum 9 passengers allowed.", [nameof(Adults)]);
        }

        // Bags should not exceed passengers with bag allowance
        var passengersWithBagAllowance = adults + children + infantsInSeat;
        if (Bags > passengersWithBagAllowance)
        {
            yield return new ValidationResult("Bags cannot exceed the number of passengers with carry-on bag allowance.", [nameof(Bags)]);
        }

        // Month validation
        if (Month is < 0 or > 12)
        {
            yield return new ValidationResult("Month must be between 0 and 12.", [nameof(Month)]);
        }

        // Travel duration validation
        if (TravelDuration is < 1 or > 3)
        {
            yield return new ValidationResult("TravelDuration must be 1 (Weekend), 2 (1 week), or 3 (2 weeks).", [nameof(TravelDuration)]);
        }

        // Travel class validation
        if (TravelClass is < 1 or > 4)
        {
            yield return new ValidationResult("TravelClass must be 1 (Economy), 2 (Premium economy), 3 (Business), or 4 (First).", [nameof(TravelClass)]);
        }

        // Type validation
        if (Type is < 1 or > 2)
        {
            yield return new ValidationResult("Type must be 1 (Round trip) or 2 (One way).", [nameof(Type)]);
        }

        // Stops validation
        if (Stops is < 0 or > 3)
        {
            yield return new ValidationResult("Stops must be 0 (Any), 1 (Nonstop only), 2 (1 stop or fewer), or 3 (2 stops or fewer).", [nameof(Stops)]);
        }
    }
}

