namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     User reviews collection
/// </summary>
public class MapsUserReviews
{
    /// <summary>
    ///     Summary snippets from reviews
    /// </summary>
    public List<MapsReviewSummary>? Summary { get; set; }

    /// <summary>
    ///     Most relevant detailed reviews
    /// </summary>
    public List<MapsReview>? MostRelevant { get; set; }
}
