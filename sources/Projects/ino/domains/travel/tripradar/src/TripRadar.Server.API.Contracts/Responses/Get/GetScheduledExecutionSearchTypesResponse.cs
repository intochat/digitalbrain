using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetScheduledExecutionSearchTypesResponse
{
    [JsonPropertyName("searchTypes")]
    public List<string> SearchTypes { get; set; } = [];
}
