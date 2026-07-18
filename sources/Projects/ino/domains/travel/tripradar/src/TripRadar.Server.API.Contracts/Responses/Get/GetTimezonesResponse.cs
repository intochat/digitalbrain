using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetTimezonesResponse
{
    [JsonPropertyName("timezones")]
    [DataMember(Name = "timezones")]
    [Required]
    public IEnumerable<TimezoneResponse> Timezones { get; set; } = null!;
}
