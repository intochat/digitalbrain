using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Requests;

public sealed class UserPreferencePatchRequestDTO
{
    [JsonPropertyName("flight")]
    public FlightPreferencesDTO? Flight { get; init; }

    [JsonPropertyName("hotel")]
    public HotelPreferencesDTO? Hotel { get; init; }

    [JsonPropertyName("event")]
    public EventPreferencesDTO? Event { get; init; }

    [JsonPropertyName("localPlaces")]
    public LocalPlacesPreferencesDTO? LocalPlaces { get; init; }

    [JsonPropertyName("maps")]
    public MapsPreferencesDTO? Maps { get; init; }

    [JsonPropertyName("placeReview")]
    public PlaceReviewPreferencesDTO? PlaceReview { get; init; }

    [JsonPropertyName("tripAdvisorSearch")]
    public TripAdvisorSearchPreferencesDTO? TripAdvisorSearch { get; init; }
}


