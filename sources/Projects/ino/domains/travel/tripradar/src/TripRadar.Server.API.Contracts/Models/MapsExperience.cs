namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Experience or tour offering
/// </summary>
public class MapsExperience
{
    /// <summary>
    ///     Experience title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Booking link
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
    ///     Rating
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    ///     Number of reviews
    /// </summary>
    public int? Reviews { get; set; }

    /// <summary>
    ///     Thumbnail image
    /// </summary>
    public string? Thumbnail { get; set; }

    /// <summary>
    ///     SerpApi thumbnail
    /// </summary>
    public string? SerpapiThumbnail { get; set; }

    /// <summary>
    ///     Source provider
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    ///     Source icon
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    ///     Duration
    /// </summary>
    public string? Duration { get; set; }
}
