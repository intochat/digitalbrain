using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetScheduledExecutionsResponse
{
    [JsonPropertyName("scheduledExecutions")]
    [DataMember(Name = "scheduledExecutions")]
    [Required]
    public List<ScheduledExecutionItem> ScheduledExecutions { get; set; } = [];
}
