namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Business post
/// </summary>
public class MapsPost
{
    /// <summary>
    ///     Post title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Media URL (image/video)
    /// </summary>
    public string? Media { get; set; }

    /// <summary>
    ///     Call to action text
    /// </summary>
    public string? Cta { get; set; }

    /// <summary>
    ///     Action link
    /// </summary>
    public string? Link { get; set; }

    /// <summary>
    ///     Phone number for calls
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    ///     Link to full post
    /// </summary>
    public string? PostLink { get; set; }

    /// <summary>
    ///     Post description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Offer duration
    /// </summary>
    public string? Duration { get; set; }

    /// <summary>
    ///     Post date
    /// </summary>
    public string? Date { get; set; }
}
