using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class ResetPasswordRequest
{
    [JsonPropertyName("username")]
    [DataMember(Name = "username")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    [DataMember(Name = "token")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Token is required.")]
    [Obfuscated]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("newPassword")]
    [DataMember(Name = "newPassword")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "New password is required.")]
    [IsPasswordValid]
    [Obfuscated]
    public string NewPassword { get; set; } = string.Empty;
}
