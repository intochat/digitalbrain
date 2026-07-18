using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class PassengerInfo
{
    [Preference(nameof(PreferenceType.Adults))]
    [JsonPropertyName("adults")]
    public int? Adults { get; set; }

    [Preference(nameof(PreferenceType.Children))]
    [JsonPropertyName("children")]
    public int? Children { get; set; }

    [Preference(nameof(PreferenceType.InfantsInSeat))]
    [JsonPropertyName("infantsInSeat")]
    public int? InfantsInSeat { get; set; }

    [Preference(nameof(PreferenceType.InfantsOnLap))]
    [JsonPropertyName("infantsOnLap")]
    public int? InfantsOnLap { get; set; }
}
