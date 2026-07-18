namespace TripRadar.Server.Application.Constants.Flights;

/// <summary>
///     Constants for Google Flights API query parameters and values
/// </summary>
public static class FlightQueryConstants
{
    /// <summary>
    ///     Sort options for flight search results
    ///     1: Top flights (default)
    ///     2: Price (lowest first)
    ///     3: Departure time
    ///     4: Arrival time
    ///     5: Duration (shortest first)
    ///     6: Emissions (lowest first)
    /// </summary>
    public static readonly string[] SortBy = ["1", "2", "3", "4", "5", "6"];

    /// <summary>
    ///     Cabin classes available for flights
    ///     1: Economy class (default)
    ///     2: Premium economy class
    ///     3: Business class
    ///     4: First class
    /// </summary>
    public static readonly string[] CabinClasses = ["1", "2", "3", "4"];

    /// <summary>
    ///     Travel classes for flight bookings
    ///     1: Economy class (default)
    ///     2: Premium economy class
    ///     3: Business class
    ///     4: First class
    /// </summary>
    public static readonly string[] TravelClasses = ["1", "2", "3", "4"];

    /// <summary>
    ///     Types of flight itineraries
    ///     1: Round trip (default)
    ///     2: One way
    ///     3: Multi-city
    /// </summary>
    public static readonly string[] FlightTypes = ["1", "2", "3"];

    /// <summary>
    ///     Number of stops during the flight
    ///     0: Any number of stops (default)
    ///     1: Nonstop only
    ///     2: 1 stop or fewer
    ///     3: 2 stops or fewer
    /// </summary>
    public static readonly string[] Stops = ["0", "1", "2", "3"];

    /// <summary>
    ///     Types of emissions information
    ///     1: Less emissions only
    /// </summary>
    public static readonly string[] EmissionsTypes = ["1"];
}
