using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class HotelAdvancedParameters
{
    public required string CheckInDate { get; set; }

    public required string CheckOutDate { get; set; }

    [Preference(nameof(PreferenceType.Adults))]
    [JsonPropertyName("adults")]
    public int? Adults { get; set; }

    [Preference(nameof(PreferenceType.Children))]
    [JsonPropertyName("children")]
    public int? Children { get; set; }

    [JsonPropertyName("childrenAges")]
    public string? ChildrenAges { get; set; }

    [Preference(nameof(PreferenceType.DefaultRooms))]
    [JsonPropertyName("defaultRooms")]
    public int? DefaultRooms { get; set; }
}
