using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class ChangePasswordRequest
{
    [JsonPropertyName("currentPassword")]
    [DataMember(Name = "currentPassword")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Current password is required.")]
    [Obfuscated]
    public string CurrentPassword { get; set; } = string.Empty;

    [JsonPropertyName("newPassword")]
    [DataMember(Name = "newPassword")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "New password is required.")]
    [IsPasswordValid]
    [Obfuscated]
    public string NewPassword { get; set; } = string.Empty;
}
