using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class UserPreferences
{
    [JsonPropertyName("Flight")]
    [DataMember(Name = "Flight")]
    public FlightPreferences? Flight { get; set; }

    [JsonPropertyName("Hotel")]
    [DataMember(Name = "Hotel")]
    public HotelPreferences? Hotel { get; set; }

    [JsonPropertyName("Event")]
    [DataMember(Name = "Event")]
    public EventPreferences? Event { get; set; }

    [JsonPropertyName("LocalPlaces")]
    [DataMember(Name = "LocalPlaces")]
    public LocalPlacesPreferences? LocalPlaces { get; set; }

    [JsonPropertyName("Maps")]
    [DataMember(Name = "Maps")]
    public MapsPreferences? Maps { get; set; }

    [JsonPropertyName("PlaceReview")]
    [DataMember(Name = "PlaceReview")]
    public PlaceReviewPreferences? PlaceReview { get; set; }

    [JsonPropertyName("TripAdvisorSearch")]
    [DataMember(Name = "TripAdvisorSearch")]
    public TripAdvisorSearchPreferences? TripAdvisorSearch { get; set; }
}
