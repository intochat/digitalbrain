using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsPagination
{
    [Range(1, 100, ErrorMessage = "Number of reviews must be between 1 and 100.")]
    public int? Num { get; set; }

    public string? NextPageToken { get; set; }
}
