using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class AdvancedSearchOptions
{
    [JsonPropertyName("type")]
    public FlightType? Type { get; set; }

    [JsonPropertyName("outboundDate")]
    public string? OutboundDate { get; set; }

    [JsonPropertyName("returnDate")]
    public string? ReturnDate { get; set; }

    [Preference(nameof(PreferenceType.TravelClass))]
    [JsonPropertyName("travelClass")]
    public TravelClassType? TravelClass { get; set; }

    [Preference(nameof(PreferenceType.PreferredCabinClass))]
    [JsonPropertyName("preferredCabinClass")]
    public TravelClassType? PreferredCabinClass { get; set; }

    [JsonPropertyName("multiCityJson")]
    public List<MultiCityLeg>? MultiCityJson { get; set; }

    [JsonPropertyName("showHidden")]
    public bool? ShowHidden { get; set; }

    [Preference(nameof(PreferenceType.DeepSearch))]
    [JsonPropertyName("deepSearch")]
    public bool? DeepSearch { get; set; }
}
