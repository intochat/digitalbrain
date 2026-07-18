using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetAirportSuggestionsResponse
{
    [JsonPropertyName("airports")]
    [DataMember(Name = "airports")]
    [Required]
    public List<AirportSuggestionItem> Airports { get; set; } = [];
}
