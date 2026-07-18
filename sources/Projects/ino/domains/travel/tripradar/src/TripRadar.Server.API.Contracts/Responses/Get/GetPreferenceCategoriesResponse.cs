using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetPreferenceCategoriesResponse
{
    [JsonPropertyName("categories")]
    public List<PreferenceCategory> Categories { get; set; } = [];
}
