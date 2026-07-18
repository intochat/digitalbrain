namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Business category information
/// </summary>
public class MapsPlaceType
{
    /// <summary>
    ///     Category name
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Number of places in this category
    /// </summary>
    public int? Places { get; set; }
}
