using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.Application.DTO.Models;

/// <summary>
/// Telegram Login Widget authentication data DTO.
/// Contains user information and hash for validation.
/// </summary>
public class TelegramAuthDataDTO
{
    /// <summary>
    /// Unique identifier for the Telegram user.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// First name of the Telegram user.
    /// </summary>
    [Obfuscated]
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = null!;

    /// <summary>
    /// Last name of the Telegram user (optional).
    /// </summary>
    [Obfuscated]
    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    /// <summary>
    /// Username of the Telegram user (optional).
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>
    /// Photo URL of the Telegram user (optional).
    /// </summary>
    [Obfuscated]
    [JsonPropertyName("photoUrl")]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Unix timestamp when the authentication was performed.
    /// </summary>
    [JsonPropertyName("authDate")]
    public long AuthDate { get; set; }

    /// <summary>
    /// HMAC-SHA256 signature of the data fields using bot token.
    /// Used to verify the authenticity of the Telegram login.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = null!;

    /// <summary>
    /// Raw initData string from Telegram Mini App WebApp.
    /// Used to validate WebApp signatures when available.
    /// </summary>
    [JsonPropertyName("rawInitData")]
    [Obfuscated]
    public string? RawInitData { get; set; }
}
