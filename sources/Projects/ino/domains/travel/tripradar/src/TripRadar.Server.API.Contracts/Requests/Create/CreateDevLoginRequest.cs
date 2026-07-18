using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public sealed record CreateDevLoginRequest(
    [property: JsonPropertyName("telegramUserId")] long TelegramUserId = 123456,
    [property: JsonPropertyName("tier")] string? Tier = null);
