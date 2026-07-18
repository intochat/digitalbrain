using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateTelegramSessionRequest
{
    [JsonPropertyName("telegramAuth")]
    [DataMember(Name = "telegramAuth")]
    public TelegramAuthData? TelegramAuth { get; set; }

    [JsonPropertyName("initData")]
    [DataMember(Name = "initData")]
    public string? InitData { get; set; }
}
