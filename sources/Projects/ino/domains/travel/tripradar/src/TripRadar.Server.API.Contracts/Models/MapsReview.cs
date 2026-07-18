namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Detailed user review
/// </summary>
public class MapsReview
{
    /// <summary>
    ///     Reviewer's username
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    ///     Rating given (1-5 stars)
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>
    ///     Google contributor ID
    /// </summary>
    public string? ContributorId { get; set; }

    /// <summary>
    ///     Full review text
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Images attached to the review
    /// </summary>
    public List<MapsImage>? Images { get; set; }

    /// <summary>
    ///     When the review was posted
    /// </summary>
    public string? Date { get; set; }
}
