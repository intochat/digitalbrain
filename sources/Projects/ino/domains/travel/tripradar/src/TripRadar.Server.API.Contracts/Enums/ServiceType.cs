using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceType
{
    [EnumMember(Value = "Event")] [Description("Event")]
    Event = 1,

    [EnumMember(Value = "Flight")] [Description("Flight")]
    Flight = 2,

    [EnumMember(Value = "Hotel")] [Description("Hotel")]
    Hotel = 3,

    [EnumMember(Value = "LocalPlaces")] [Description("LocalPlaces")]
    LocalPlaces = 4,

    [EnumMember(Value = "Maps")] [Description("Maps")]
    Maps = 5,

    [EnumMember(Value = "PlaceReview")] [Description("PlaceReview")]
    PlaceReview = 6,

    [EnumMember(Value = "FlightExplore")] [Description("FlightExplore")]
    FlightExplore = 7,

    [EnumMember(Value = "TripAdvisorSearch")] [Description("TripAdvisorSearch")]
    TripAdvisorSearch = 8,

    [EnumMember(Value = "TripAdvisorPlace")] [Description("TripAdvisorPlace")]
    TripAdvisorPlace = 9,

    [EnumMember(Value = "OpenTableReview")] [Description("OpenTableReview")]
    OpenTableReview = 10,

    [EnumMember(Value = "YelpSearch")] [Description("YelpSearch")]
    YelpSearch = 12,

    [EnumMember(Value = "YelpPlace")] [Description("YelpPlace")]
    YelpPlace = 13,

    [EnumMember(Value = "YelpReviews")] [Description("YelpReviews")]
    YelpReviews = 14,

    [EnumMember(Value = "YelpPlaceFullMenu")] [Description("YelpPlaceFullMenu")]
    YelpPlaceFullMenu = 15,

    [EnumMember(Value = "MapsDirections")] [Description("MapsDirections")]
    MapsDirections = 16,

    [EnumMember(Value = "MapsPlaceResults")] [Description("MapsPlaceResults")]
    MapsPlaceResults = 17,

    [EnumMember(Value = "GoogleLightSearch")] [Description("GoogleLightSearch")]
    GoogleLightSearch = 18
}
