namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Live busyness information
/// </summary>
public class MapsLiveHash
{
    /// <summary>
    ///     Current busyness description
    /// </summary>
    public string? Info { get; set; }

    /// <summary>
    ///     Typical time spent at location
    /// </summary>
    public string? TimeSpent { get; set; }
}
