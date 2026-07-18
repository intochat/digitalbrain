namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Weather information
/// </summary>
public class MapsWeather
{
    /// <summary>
    ///     Temperature in Celsius
    /// </summary>
    public string? Celsius { get; set; }

    /// <summary>
    ///     Temperature in Fahrenheit
    /// </summary>
    public string? Fahrenheit { get; set; }

    /// <summary>
    ///     Weather conditions
    /// </summary>
    public string? Conditions { get; set; }
}
