namespace Ino.Domains.Travel.Rfw;

/// <summary>
/// Mock-data shapes for the RFW card builders. Each record is the
/// in-memory analogue of a future tripradar GraphQL response — names and
/// shapes match what the eventual <c>find_flights</c> / <c>find_hotels</c>
/// / <c>find_places</c> / <c>find_events</c> / <c>get_weather</c> tool
/// methods will return when the LlmNeuron rewrite swaps in real APIs.
/// Keeping the shapes addressable now means that swap is mechanical.
/// </summary>
internal sealed record FlightOption(
    string Id,
    string Airline,
    string Origin,
    string Destination,
    decimal Price,
    int DurationMin);

internal sealed record HotelOption(
    string Id,
    string Name,
    decimal PricePerNight,
    double Rating,
    string City);

internal sealed record PlaceOption(
    string Id,
    string Name,
    string Category,
    double Rating);

/// <summary>
/// Climatology summary — what's the weather *typically* like at this
/// destination in this month. Used by the dates-refinement hop to set
/// expectations before the user commits. Real impl: Open-Meteo
/// <c>/climate</c> or tripradar wrapper.
/// </summary>
internal sealed record WeatherClimatology(
    string Destination,
    string Month,
    string Season,           // "dry" | "wet" | "shoulder"
    int AvgTempC,
    double RainProbability); // 0.0..1.0

/// <summary>
/// Forecast for a single date. Used by the weather-aware activity hop to
/// label outdoor picks. Real impl: Open-Meteo <c>/forecast</c>.
/// </summary>
internal sealed record WeatherForecast(
    string Location,
    DateOnly Date,
    string Condition,        // "sunny" | "cloudy" | "rain"
    int TempC,
    double RainProbability);

/// <summary>
/// Mirrors tripradar's <c>events</c> GraphQL shape (title / date / venue
/// / ticket info). v0.1 uses canned events per destination; the real
/// integration calls tripradar with destination + dateRange.
/// </summary>
internal sealed record EventOption(
    string Id,
    string Title,
    string DateLabel,        // "Sat, Jun 14" — display string
    string VenueName,
    string Category,         // "music" | "exhibit" | "food" | ...
    string TicketSummary,    // "From $35" or "Free"
    string Description);

/// <summary>
/// A place-to-visit suggestion enriched with indoor/outdoor classification
/// and (optionally) the forecast for when the user might visit. Drives
/// the rain-aware activity ranking in the activity hop.
/// </summary>
internal sealed record ActivityOption(
    string Id,
    string Name,
    string Category,
    double Rating,
    bool IsIndoor,
    string WeatherBadge);    // "Sunny day pick" | "Rainy day pick" | "All weather"
