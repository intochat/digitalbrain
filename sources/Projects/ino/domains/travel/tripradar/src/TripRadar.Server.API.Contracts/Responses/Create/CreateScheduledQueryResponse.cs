using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class CreateScheduledQueryResponse
{
    [JsonPropertyName("scheduledExecutionUniqueId")]
    [DataMember(Name = "scheduledExecutionUniqueId")]
    public required Guid ScheduledExecutionUniqueId { get; set; }
}
