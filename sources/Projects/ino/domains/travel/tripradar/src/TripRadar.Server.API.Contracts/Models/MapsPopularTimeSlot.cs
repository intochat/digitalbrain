namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Busyness information for a specific time slot
/// </summary>
public class MapsPopularTimeSlot
{
    /// <summary>
    ///     Time of day (e.g., "9 AM")
    /// </summary>
    public string? Time { get; set; }

    /// <summary>
    ///     Busyness description
    /// </summary>
    public string? Info { get; set; }

    /// <summary>
    ///     Busyness score (0-100)
    /// </summary>
    public int? BusynessScore { get; set; }
}
