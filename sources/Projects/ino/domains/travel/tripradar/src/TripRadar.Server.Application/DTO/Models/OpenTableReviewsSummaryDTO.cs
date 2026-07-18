using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class OpenTableReviewsSummaryDTO
{
    [JsonPropertyName("reviews_count")] public int ReviewsCount { get; set; }

    [JsonPropertyName("ratings_count")] public int RatingsCount { get; set; }

    [JsonPropertyName("ratings_summary")]
    public OpenTableRatingsSummaryDTO? RatingsSummary { get; set; }

    [JsonPropertyName("ratings")]
    public List<OpenTableRatingBreakdownDTO>? Ratings { get; set; }

    [JsonPropertyName("ai_summary")] public string? AiSummary { get; set; }
}

public class OpenTableRatingsSummaryDTO
{
    [JsonPropertyName("overall")] public double? Overall { get; set; }

    [JsonPropertyName("food")] public double? Food { get; set; }

    [JsonPropertyName("service")] public double? Service { get; set; }

    [JsonPropertyName("ambience")] public double? Ambience { get; set; }

    [JsonPropertyName("value")] public double? Value { get; set; }

    [JsonPropertyName("noise")] public string? Noise { get; set; }
}

public class OpenTableRatingBreakdownDTO
{
    [JsonPropertyName("stars")] public int Stars { get; set; }

    [JsonPropertyName("count")] public int Count { get; set; }
}
