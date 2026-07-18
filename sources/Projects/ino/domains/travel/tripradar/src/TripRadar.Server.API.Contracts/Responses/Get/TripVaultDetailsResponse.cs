using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class TripVaultDetailsResponse
{
    [JsonPropertyName("uniqueId")]
    [DataMember(Name = "uniqueId")]
    public Guid UniqueId { get; set; }

    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    [DataMember(Name = "description")]
    public string? Description { get; set; }

    [JsonPropertyName("startDate")]
    [DataMember(Name = "startDate")]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [DataMember(Name = "endDate")]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("items")]
    [DataMember(Name = "items")]
    public IList<TripItemResponse> Items { get; set; } = new List<TripItemResponse>();

    [JsonPropertyName("createdOn")]
    [DataMember(Name = "createdOn")]
    public DateTime CreatedOn { get; set; }
}
