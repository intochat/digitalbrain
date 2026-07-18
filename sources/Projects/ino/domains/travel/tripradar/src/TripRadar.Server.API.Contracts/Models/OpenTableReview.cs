using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class OpenTableReview
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("content")] public string? Content { get; set; }

    [JsonPropertyName("submitted_at")]
    public string? SubmittedAt { get; set; }

    [JsonPropertyName("dined_at")] public string? DinedAt { get; set; }

    [JsonPropertyName("rating")] public OpenTableReviewRatings? Rating { get; set; }

    [JsonPropertyName("ratings")] public OpenTableReviewRatings? Ratings { get; set; }

    [JsonPropertyName("user")] public OpenTableReviewUser? User { get; set; }

    [JsonPropertyName("helpfulness")]
    public OpenTableReviewHelpfulness? Helpfulness { get; set; }

    [JsonPropertyName("images")] public List<OpenTableReviewImage>? Images { get; set; }

    [JsonPropertyName("response")]
    public OpenTableReviewResponse? Response { get; set; }
}

public class OpenTableReviewRatings
{
    [JsonPropertyName("overall")] public int? Overall { get; set; }

    [JsonPropertyName("food")] public int? Food { get; set; }

    [JsonPropertyName("service")] public int? Service { get; set; }

    [JsonPropertyName("ambience")] public int? Ambience { get; set; }

    [JsonPropertyName("value")] public int? Value { get; set; }

    [JsonPropertyName("noise")] public string? Noise { get; set; }
}

public class OpenTableReviewUser
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("number_of_reviews")] public int? NumberOfReviews { get; set; }

    [JsonPropertyName("location")] public string? Location { get; set; }

    [JsonPropertyName("avatar")] public string? Avatar { get; set; }

    [JsonPropertyName("vip")] public bool? Vip { get; set; }
}

public class OpenTableReviewHelpfulness
{
    [JsonPropertyName("up")] public int? Up { get; set; }

    [JsonPropertyName("score")] public int? Score { get; set; }
}

public class OpenTableReviewImage
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }

    [JsonPropertyName("variants")] public List<OpenTableReviewImageVariant>? Variants { get; set; }
}

public class OpenTableReviewImageVariant
{
    [JsonPropertyName("size")] public string? Size { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class OpenTableReviewResponse
{
    [JsonPropertyName("content")] public string? Content { get; set; }

    [JsonPropertyName("date")] public string? Date { get; set; }
}
