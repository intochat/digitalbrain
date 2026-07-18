using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class ActivateUserRequest
{
    [JsonPropertyName("email")]
    [DataMember(Name = "email")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [Obfuscated]
    public string Email { get; set; } = null!;

    [JsonPropertyName("telegramAuth")]
    [DataMember(Name = "telegramAuth")]
    [Required(ErrorMessage = "Telegram authentication data is required.")]
    public TelegramAuthData TelegramAuth { get; set; } = null!;
}
