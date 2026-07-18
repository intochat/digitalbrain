namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Popular times and busy periods
/// </summary>
public class MapsPopularTimes
{
    /// <summary>
    ///     Hourly busyness data by day of week
    /// </summary>
    public Dictionary<string, List<MapsPopularTimeSlot>>? GraphResults { get; set; }

    /// <summary>
    ///     Current live busyness information
    /// </summary>
    public MapsLiveHash? LiveHash { get; set; }
}
