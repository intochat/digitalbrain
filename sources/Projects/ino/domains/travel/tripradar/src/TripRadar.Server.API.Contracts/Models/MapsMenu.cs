namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Menu information for restaurants
/// </summary>
public class MapsMenu
{
    /// <summary>
    ///     Link to the menu
    /// </summary>
    public string? Link { get; set; }

    /// <summary>
    ///     Source of the menu data
    /// </summary>
    public string? Source { get; set; }
}
