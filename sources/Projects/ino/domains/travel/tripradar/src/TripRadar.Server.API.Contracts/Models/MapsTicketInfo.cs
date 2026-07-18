namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Ticket information
/// </summary>
public class MapsTicketInfo
{
    /// <summary>
    ///     Ticket price
    /// </summary>
    public string? Price { get; set; }

    /// <summary>
    ///     Numeric price value
    /// </summary>
    public double? ExtractedPrice { get; set; }

    /// <summary>
    ///     Link to purchase tickets
    /// </summary>
    public string? Link { get; set; }

    /// <summary>
    ///     Ticket source (e.g., "Ticketmaster")
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    ///     Icon for ticket source
    /// </summary>
    public string? SourceIcon { get; set; }
}
