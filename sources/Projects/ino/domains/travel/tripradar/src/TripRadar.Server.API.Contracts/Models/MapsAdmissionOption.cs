namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Specific admission option
/// </summary>
public class MapsAdmissionOption
{
    /// <summary>
    ///     Ticket type or description
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Purchase link
    /// </summary>
    public string? Link { get; set; }

    /// <summary>
    ///     Price
    /// </summary>
    public string? Price { get; set; }

    /// <summary>
    ///     Numeric price
    /// </summary>
    public double? ExtractedPrice { get; set; }

    /// <summary>
    ///     Whether this is the official site
    /// </summary>
    public bool? OfficialSite { get; set; }

    /// <summary>
    ///     Additional features (instant confirmation, mobile ticket, etc.)
    /// </summary>
    public List<string>? Extensions { get; set; }
}
