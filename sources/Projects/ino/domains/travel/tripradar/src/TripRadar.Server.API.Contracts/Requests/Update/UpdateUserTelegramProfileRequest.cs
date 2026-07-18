using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class UpdateUserTelegramProfileRequest
{
    [JsonPropertyName("telegramUserId")]
    [DataMember(Name = "telegramUserId")]
    [Range(1, long.MaxValue, ErrorMessage = "telegramUserId must be a positive number.")]
    public long TelegramUserId { get; set; }
}