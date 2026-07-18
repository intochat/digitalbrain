using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class UserPreference
{
    [JsonPropertyName("preferenceTypeDisplayName")]
    [DataMember(Name = "preferenceTypeDisplayName")]
    [Required]
    public string PreferenceTypeDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    [DataMember(Name = "value")]
    [Required]
    public string Value { get; set; } = string.Empty;
}
