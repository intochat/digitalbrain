namespace TripRadar.Server.Mocks.Responses.SerpApi.Maps;

/// <summary>
///     Mock response model for Google Maps API via SerpApi
/// </summary>
public class GoogleMapsResponse
{
    public SearchMetadata SearchMetadata { get; set; } = new();
    public MapsSearchParameters SearchParameters { get; set; } = new();
    public MapsPlaceResult? PlaceResults { get; set; }
}

/// <summary>
///     Mock search metadata
/// </summary>
public class SearchMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string JsonEndpoint { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string ProcessedAt { get; set; } = string.Empty;
    public string RawHtmlFile { get; set; } = string.Empty;
    public double TotalTimeTaken { get; set; }
}

/// <summary>
///     Mock search parameters
/// </summary>
public class MapsSearchParameters
{
    public string Engine { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public string GoogleDomain { get; set; } = string.Empty;
    public string Hl { get; set; } = string.Empty;
    public string Gl { get; set; } = string.Empty;
}

/// <summary>
///     Mock place result
/// </summary>
public class MapsPlaceResult
{
    public string Title { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public string? DataId { get; set; }
    public string? DataCid { get; set; }
    public string? ReviewsLink { get; set; }
    public string? PhotosLink { get; set; }
    public GpsCoordinates? GpsCoordinates { get; set; }
    public string? PlaceIdSearch { get; set; }
    public string? ProviderId { get; set; }
    public string? Thumbnail { get; set; }
    public string? SerpapiThumbnail { get; set; }
    public double? Rating { get; set; }
    public int? Reviews { get; set; }
    public string? Price { get; set; }
    public List<string>? Type { get; set; }
    public List<string>? TypeIds { get; set; }
    public string? Description { get; set; }
    public MapsMenu? Menu { get; set; }
    public string? OrderOnlineLink { get; set; }
    public ServiceOptions? ServiceOptions { get; set; }
    public List<MapsExtension>? Extensions { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string? OpenState { get; set; }
    public string? PlusCode { get; set; }
    public List<Dictionary<string, string>>? Hours { get; set; }
    public Dictionary<string, string>? OperatingHours { get; set; }
    public List<MapsImage>? Images { get; set; }
    public MapsUserReviews? UserReviews { get; set; }
    public MapsPopularTimes? PopularTimes { get; set; }
    public string? BookingLink { get; set; }
    public List<MapsEvent>? Events { get; set; }
    public List<MapsQA>? QuestionsAndAnswers { get; set; }
}

/// <summary>
///     Mock GPS coordinates
/// </summary>
public class GpsCoordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

/// <summary>
///     Mock menu information
/// </summary>
public class MapsMenu
{
    public string? Link { get; set; }
    public string? Source { get; set; }
}

/// <summary>
///     Mock service options
/// </summary>
public class ServiceOptions
{
    public bool? DineIn { get; set; }
    public bool? Takeout { get; set; }
    public bool? Delivery { get; set; }
    public bool? Reservations { get; set; }
}

/// <summary>
///     Mock extension information
/// </summary>
public class MapsExtension
{
    public List<string>? Highlights { get; set; }
    public List<string>? PopularFor { get; set; }
    public List<string>? Accessibility { get; set; }
    public List<string>? Crowd { get; set; }
    public List<string>? Payments { get; set; }
    public List<string>? Planning { get; set; }
}

/// <summary>
///     Mock image information
/// </summary>
public class MapsImage
{
    public string? Title { get; set; }
    public string? Thumbnail { get; set; }
}

/// <summary>
///     Mock user reviews
/// </summary>
public class MapsUserReviews
{
    public List<MapsReviewSummary>? Summary { get; set; }
    public List<MapsReview>? MostRelevant { get; set; }
}

/// <summary>
///     Mock review summary
/// </summary>
public class MapsReviewSummary
{
    public string? Snippet { get; set; }
}

/// <summary>
///     Mock review
/// </summary>
public class MapsReview
{
    public string? Username { get; set; }
    public int? Rating { get; set; }
    public string? ContributorId { get; set; }
    public string? Description { get; set; }
    public string? Date { get; set; }
}

/// <summary>
///     Mock popular times
/// </summary>
public class MapsPopularTimes
{
    public MapsLiveHash? LiveHash { get; set; }
}

/// <summary>
///     Mock live hash
/// </summary>
public class MapsLiveHash
{
    public string? Info { get; set; }
    public string? TimeSpent { get; set; }
}

/// <summary>
///     Mock event
/// </summary>
public class MapsEvent
{
    public string? Title { get; set; }
}

/// <summary>
///     Mock Q&A
/// </summary>
public class MapsQA
{
    public MapsQuestion? Question { get; set; }
    public MapsAnswer? Answer { get; set; }
    public int? TotalAnswers { get; set; }
}

/// <summary>
///     Mock question
/// </summary>
public class MapsQuestion
{
    public MapsUser? User { get; set; }
    public string? Text { get; set; }
    public string? Date { get; set; }
}

/// <summary>
///     Mock answer
/// </summary>
public class MapsAnswer
{
    public MapsUser? User { get; set; }
    public string? Text { get; set; }
    public string? Date { get; set; }
}

/// <summary>
///     Mock user
/// </summary>
public class MapsUser
{
    public string? Name { get; set; }
    public int? LocalGuideLevel { get; set; }
}
