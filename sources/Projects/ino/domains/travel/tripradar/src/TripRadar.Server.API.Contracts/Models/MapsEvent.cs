namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Event information
/// </summary>
public class MapsEvent
{
    /// <summary>
    ///     Event title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Event date and time information
    /// </summary>
    public MapsEventDate? Date { get; set; }

    /// <summary>
    ///     Event thumbnail image
    /// </summary>
    public string? Thumbnail { get; set; }

    /// <summary>
    ///     Ticket purchasing information
    /// </summary>
    public MapsTicketInfo? TicketInfo { get; set; }
}
