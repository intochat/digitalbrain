using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class TripItemResponse
{
    [JsonPropertyName("uniqueId")]
    [DataMember(Name = "uniqueId")]
    public Guid UniqueId { get; set; }

    [JsonPropertyName("serviceType")]
    [DataMember(Name = "serviceType")]
    public ServiceType ServiceType { get; set; }

    [JsonPropertyName("queryParametersJson")]
    [DataMember(Name = "queryParametersJson")]
    public string QueryParametersJson { get; set; } = null!;

    [JsonPropertyName("startDateTime")]
    [DataMember(Name = "startDateTime")]
    public DateTime? StartDateTime { get; set; }

    [JsonPropertyName("endDateTime")]
    [DataMember(Name = "endDateTime")]
    public DateTime? EndDateTime { get; set; }

    [JsonPropertyName("resultSummary")]
    [DataMember(Name = "resultSummary")]
    public string? ResultSummary { get; set; }

    [JsonPropertyName("createdOn")]
    [DataMember(Name = "createdOn")]
    public DateTime CreatedOn { get; set; }
}
