using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetRecentSearchesResponse
{
    [JsonPropertyName("recentSearches")]
    [DataMember(Name = "recentSearches")]
    public IList<RecentSearchItemResponse> RecentSearches { get; set; } = [];
}
