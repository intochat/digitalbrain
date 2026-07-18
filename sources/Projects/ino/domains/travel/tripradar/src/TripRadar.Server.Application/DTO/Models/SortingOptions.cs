using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class SortingOptions
{
    [Preference(nameof(PreferenceType.SortBy))]
    [JsonPropertyName("sortBy")]
    public SortBy? SortBy { get; set; }
}
