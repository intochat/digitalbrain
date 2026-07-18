using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class TimezoneResponse
{
    [JsonPropertyName("timezoneId")]
    [DataMember(Name = "timezoneId")]
    [Required]
    public int TimezoneId { get; set; }

    [JsonPropertyName("timezoneCode")]
    [DataMember(Name = "timezoneCode")]
    [Required]
    public string TimezoneCode { get; set; } = null!;

    [JsonPropertyName("timezoneName")]
    [DataMember(Name = "timezoneName")]
    [Required]
    public string TimezoneName { get; set; } = null!;
}
