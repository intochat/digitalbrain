using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class UpdateScheduledExecutionConfigurationRequest
{
    [JsonPropertyName("isActive")]
    [DataMember(Name = "isActive")]
    [Required(ErrorMessage = "IsActive flag is required.")]
    public bool IsActive { get; set; }

    [JsonPropertyName("schedule")]
    [DataMember(Name = "schedule")]
    public string? Schedule { get; set; }

    [JsonPropertyName("nextExecutionTime")]
    [DataMember(Name = "nextExecutionTime")]
    [DataType(DataType.DateTime)]
    public DateTime? NextExecutionTime { get; set; }
}
