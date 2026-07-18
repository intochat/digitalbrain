namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Image information
/// </summary>
public class MapsImage
{
    /// <summary>
    ///     Image category or title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Thumbnail URL
    /// </summary>
    public string? Thumbnail { get; set; }
}
