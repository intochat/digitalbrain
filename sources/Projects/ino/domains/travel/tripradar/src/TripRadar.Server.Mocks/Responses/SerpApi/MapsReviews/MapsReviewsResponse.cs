using System.Text.Json.Serialization;

namespace TripRadar.Server.Mocks.Responses.SerpApi.MapsReviews;

public class GoogleMapsReviewsResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public SearchParameters? SearchParameters { get; set; }

    [JsonPropertyName("place_info")] public PlaceInfo? PlaceInfo { get; set; }

    [JsonPropertyName("topics")] public List<Topic>? Topics { get; set; }

    [JsonPropertyName("reviews")] public List<Review>? Reviews { get; set; }

    [JsonPropertyName("pagination")] public Pagination? Pagination { get; set; }
}

public class SearchMetadata
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("json_endpoint")] public string? JsonEndpoint { get; set; }

    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }

    [JsonPropertyName("processed_at")] public string? ProcessedAt { get; set; }

    [JsonPropertyName("google_maps_reviews_url")]
    public string? GoogleMapsReviewsUrl { get; set; }

    [JsonPropertyName("google_url")] public string? GoogleUrl { get; set; }

    [JsonPropertyName("raw_html_file")] public string? RawHtmlFile { get; set; }

    [JsonPropertyName("prettify_html_file")]
    public string? PrettifyHtmlFile { get; set; }

    [JsonPropertyName("total_time_taken")] public double TotalTimeTaken { get; set; }
}

public class SearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("data_id")] public string? DataId { get; set; }

    [JsonPropertyName("sort_by")] public string? SortBy { get; set; }

    [JsonPropertyName("topic_id")] public string? TopicId { get; set; }

    [JsonPropertyName("hl")] public string? Hl { get; set; }
}

public class PlaceInfo
{
    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("data_id")] public string? DataId { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("gps_coordinates")] public GpsCoordinates? GpsCoordinates { get; set; }

    [JsonPropertyName("rating")] public double Rating { get; set; }

    [JsonPropertyName("reviews")] public int Reviews { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("address")] public string? Address { get; set; }

    [JsonPropertyName("phone")] public string? Phone { get; set; }

    [JsonPropertyName("website")] public string? Website { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }
}

public class Topic
{
    [JsonPropertyName("keyword")] public string? Keyword { get; set; }

    [JsonPropertyName("mentions")] public int Mentions { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }
}

public class Review
{
    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("position")] public int Position { get; set; }

    [JsonPropertyName("user")] public User? User { get; set; }

    [JsonPropertyName("rating")] public double Rating { get; set; }

    [JsonPropertyName("date")] public string? Date { get; set; }

    [JsonPropertyName("iso_date")] public string? IsoDate { get; set; }

    [JsonPropertyName("iso_date_of_last_edit")]
    public string? IsoDateOfLastEdit { get; set; }

    [JsonPropertyName("snippet")] public string? Snippet { get; set; }

    [JsonPropertyName("extracted_snippet")]
    public ExtractedSnippet? ExtractedSnippet { get; set; }

    [JsonPropertyName("likes")] public int? Likes { get; set; }

    [JsonPropertyName("images")] public List<string>? Images { get; set; }

    [JsonPropertyName("source")] public string? Source { get; set; }

    [JsonPropertyName("review_id")] public string? ReviewId { get; set; }

    [JsonPropertyName("local_guide")] public bool? LocalGuide { get; set; }

    [JsonPropertyName("details")] public ReviewDetails? Details { get; set; }

    [JsonPropertyName("response")] public OwnerResponse? Response { get; set; }

    [JsonPropertyName("response_from_owner_text")]
    public string? ResponseFromOwnerText { get; set; }

    [JsonPropertyName("response_from_owner_ago")]
    public string? ResponseFromOwnerAgo { get; set; }
}

public class User
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("contributor_id")] public string? ContributorId { get; set; }

    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }

    [JsonPropertyName("local_guide")] public bool? LocalGuide { get; set; }

    [JsonPropertyName("reviews")] public int? Reviews { get; set; }

    [JsonPropertyName("photos")] public int? Photos { get; set; }
}

public class GpsCoordinates
{
    [JsonPropertyName("latitude")] public double Latitude { get; set; }

    [JsonPropertyName("longitude")] public double Longitude { get; set; }
}

public class Pagination
{
    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public SerpApiPagination? SerpApiPagination { get; set; }
}

public class SerpApiPagination
{
    [JsonPropertyName("next")] public string? Next { get; set; }

    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
}

public class ExtractedSnippet
{
    [JsonPropertyName("original")] public string? Original { get; set; }
}

public class ReviewDetails
{
    [JsonPropertyName("service")] public object? Service { get; set; }

    [JsonPropertyName("meal_type")] public string? MealType { get; set; }

    [JsonPropertyName("price_per_person")] public string? PricePerPerson { get; set; }

    [JsonPropertyName("food")] public object? Food { get; set; }

    [JsonPropertyName("atmosphere")] public object? Atmosphere { get; set; }

    [JsonPropertyName("recommended_dishes")]
    public string? RecommendedDishes { get; set; }

    [JsonPropertyName("vegetarian_options")]
    public string? VegetarianOptions { get; set; }

    [JsonPropertyName("dietary_restrictions")]
    public string? DietaryRestrictions { get; set; }

    [JsonPropertyName("kid_friendliness")] public string? KidFriendliness { get; set; }

    [JsonPropertyName("wheelchair_accessibility")]
    public string? WheelchairAccessibility { get; set; }
}

public class OwnerResponse
{
    [JsonPropertyName("date")] public string? Date { get; set; }

    [JsonPropertyName("iso_date")] public string? IsoDate { get; set; }

    [JsonPropertyName("iso_date_of_last_edit")]
    public string? IsoDateOfLastEdit { get; set; }

    [JsonPropertyName("snippet")] public string? Snippet { get; set; }

    [JsonPropertyName("extracted_snippet")]
    public ExtractedSnippet? ExtractedSnippet { get; set; }
}
