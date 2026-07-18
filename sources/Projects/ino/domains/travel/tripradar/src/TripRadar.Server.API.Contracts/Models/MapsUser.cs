namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     User information
/// </summary>
public class MapsUser
{
    /// <summary>
    ///     User's display name
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Link to user profile
    /// </summary>
    public string? Link { get; set; }

    /// <summary>
    ///     Google Local Guide level
    /// </summary>
    public int? LocalGuideLevel { get; set; }

    /// <summary>
    ///     User's profile picture
    /// </summary>
    public string? Thumbnail { get; set; }
}
