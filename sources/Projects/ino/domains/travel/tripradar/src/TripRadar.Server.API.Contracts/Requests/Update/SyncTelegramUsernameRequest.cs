using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class SyncTelegramUsernameRequest
{
    [JsonPropertyName("telegramAuth")]
    [DataMember(Name = "telegramAuth")]
    [Required(ErrorMessage = "Telegram authentication data is required.")]
    public TelegramAuthData TelegramAuth { get; set; } = null!;
}
