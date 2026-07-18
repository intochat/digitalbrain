using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class TripVaultResponse
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

    [JsonPropertyName("itemsCount")]
    [DataMember(Name = "itemsCount")]
    public int ItemsCount { get; set; }

    [JsonPropertyName("createdOn")]
    [DataMember(Name = "createdOn")]
    public DateTime CreatedOn { get; set; }
}
