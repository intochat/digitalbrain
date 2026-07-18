using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Convertors;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsPlaceResultDTO
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("place_id")]
    public string PlaceId { get; set; } = string.Empty;

    [JsonPropertyName("data_id")]
    public string? DataId { get; set; }

    [JsonPropertyName("data_cid")]
    public string? DataCid { get; set; }

    [JsonPropertyName("reviews_link")]
    public string? ReviewsLink { get; set; }

    [JsonPropertyName("photos_link")]
    public string? PhotosLink { get; set; }

    [JsonPropertyName("gps_coordinates")]
    public GpsCoordinatesDTO? GpsCoordinates { get; set; }

    [JsonPropertyName("place_id_search")]
    public string? PlaceIdSearch { get; set; }

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("serpapi_thumbnail")]
    public string? SerpapiThumbnail { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("reviews")]
    public int? Reviews { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("type_ids")]
    public List<string>? TypeIds { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("menu")]
    public MapsMenuDTO? Menu { get; set; }

    [JsonPropertyName("order_online_link")]
    public string? OrderOnlineLink { get; set; }

    [JsonPropertyName("service_options")]
    public ServiceOptionsDTO? ServiceOptions { get; set; }

    [JsonPropertyName("extensions")]
    public List<MapsExtensionDTO>? Extensions { get; set; }

    [JsonPropertyName("unsupported_extensions")]
    public List<MapsExtensionDTO>? UnsupportedExtensions { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("open_state")]
    public string? OpenState { get; set; }

    [JsonPropertyName("plus_code")]
    public string? PlusCode { get; set; }

    [JsonPropertyName("hours")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string? Hours { get; set; }

    [JsonPropertyName("operating_hours")] public Dictionary<string, string>? OperatingHours { get; set; }

    [JsonPropertyName("images")]
    public List<MapsImageDTO>? Images { get; set; }

    [JsonPropertyName("user_reviews")]
    public MapsUserReviewsDTO? UserReviews { get; set; }

    [JsonPropertyName("people_also_search_for")]
    public List<MapsRelatedSearchDTO>? PeopleAlsoSearchFor { get; set; }

    [JsonPropertyName("popular_times")]
    public MapsPopularTimesDTO? PopularTimes { get; set; }

    [JsonPropertyName("booking_link")]
    public string? BookingLink { get; set; }

    [JsonPropertyName("reserve_a_table")]
    public string? ReserveATable { get; set; }

    [JsonPropertyName("order_online")]
    public string? OrderOnline { get; set; }

    [JsonPropertyName("events")]
    public List<MapsEventDTO>? Events { get; set; }

    [JsonPropertyName("questions_and_answers")]
    public List<MapsQADTO>? QuestionsAndAnswers { get; set; }

    [JsonPropertyName("at_this_place")]
    public MapsAtThisPlaceDTO? AtThisPlace { get; set; }

    [JsonPropertyName("admission")]
    public List<MapsAdmissionDTO>? Admission { get; set; }

    [JsonPropertyName("experiences")]
    public List<MapsExperienceDTO>? Experiences { get; set; }

    [JsonPropertyName("posts")]
    public List<MapsPostDTO>? Posts { get; set; }

    [JsonPropertyName("serpapi_posts_link")]
    public string? SerpapiPostsLink { get; set; }

    [JsonPropertyName("weather")]
    public MapsWeatherDTO? Weather { get; set; }

    [JsonPropertyName("at_this_location")]
    public List<MapsAtLocationDTO>? AtThisLocation { get; set; }
}
