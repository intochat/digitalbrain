using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetAirlinesResponse
{
    [JsonPropertyName("airlines")]
    [DataMember(Name = "airlines")]
    [Required]
    public IEnumerable<AirlineResponse> Airlines { get; set; } = null!;
}
