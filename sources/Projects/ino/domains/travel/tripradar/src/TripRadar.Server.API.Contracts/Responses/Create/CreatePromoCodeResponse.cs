using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class CreatePromoCodeResponse
{
    [JsonPropertyName("message")]
    [DataMember(Name = "message")]
    [Required]
    public string Message { get; set; } = null!;
}
