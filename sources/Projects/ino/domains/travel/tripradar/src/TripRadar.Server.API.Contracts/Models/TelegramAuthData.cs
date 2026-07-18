using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Models;

public class TelegramAuthData
{
    [JsonPropertyName("id")]
    [DataMember(Name = "id")]
    [Required]
    public long Id { get; set; }

    [JsonPropertyName("firstName")]
    [DataMember(Name = "firstName")]
    [Required(AllowEmptyStrings = false)]
    [Obfuscated]
    public string FirstName { get; set; } = null!;

    [JsonPropertyName("lastName")]
    [DataMember(Name = "lastName")]
    [Obfuscated]
    public string? LastName { get; set; }

    [JsonPropertyName("username")]
    [DataMember(Name = "username")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Username is required.")]
    [StringLength(ValidationConstants.MaxUsernameLength, MinimumLength = ValidationConstants.MinUsernameLength, ErrorMessage = "Username must be between 3 and 50 characters.")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("photoUrl")]
    [DataMember(Name = "photoUrl")]
    [Obfuscated]
    public string? PhotoUrl { get; set; }

    [JsonPropertyName("authDate")]
    [DataMember(Name = "authDate")]
    [Required]
    public long AuthDate { get; set; }

    [JsonPropertyName("hash")]
    [DataMember(Name = "hash")]
    [Required(AllowEmptyStrings = false)]
    public string Hash { get; set; } = null!;

    [JsonPropertyName("rawInitData")]
    [DataMember(Name = "rawInitData")]
    [Obfuscated]
    public string? RawInitData { get; set; }
}
