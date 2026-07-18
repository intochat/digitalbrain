using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetLocationSuggestionsResponse
{
    [JsonPropertyName("locations")]
    [DataMember(Name = "locations")]
    [Required]
    public List<LocationSuggestionItem> Locations { get; set; } = [];
}
