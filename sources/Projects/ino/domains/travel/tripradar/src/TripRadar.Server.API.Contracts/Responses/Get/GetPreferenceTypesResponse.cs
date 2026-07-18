using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetPreferenceTypesResponse
{
    [JsonPropertyName("preferenceTypes")]
    public List<PreferenceType> PreferenceTypes { get; set; } = new();
}