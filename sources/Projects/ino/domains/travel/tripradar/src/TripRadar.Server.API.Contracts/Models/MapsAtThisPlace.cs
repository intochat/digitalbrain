namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Other businesses at this location
/// </summary>
public class MapsAtThisPlace
{
    /// <summary>
    ///     Categories of businesses
    /// </summary>
    public List<MapsPlaceType>? Type { get; set; }

    /// <summary>
    ///     List of businesses
    /// </summary>
    public List<MapsSubPlace>? Places { get; set; }
}
