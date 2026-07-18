namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Event date information
/// </summary>
public class MapsEventDate
{
    /// <summary>
    ///     Start date
    /// </summary>
    public string? StartDate { get; set; }

    /// <summary>
    ///     Start time
    /// </summary>
    public string? StartTime { get; set; }

    /// <summary>
    ///     Combined date and time string
    /// </summary>
    public string? When { get; set; }
}
