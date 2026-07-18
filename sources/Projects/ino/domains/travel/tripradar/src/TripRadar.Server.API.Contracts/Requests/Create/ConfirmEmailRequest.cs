using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class ConfirmEmailRequest
{
    [JsonPropertyName("email")]
    [DataMember(Name = "email")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [Obfuscated]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    [DataMember(Name = "token")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Token is required.")]
    [Obfuscated]
    public string Token { get; set; } = string.Empty;
}
