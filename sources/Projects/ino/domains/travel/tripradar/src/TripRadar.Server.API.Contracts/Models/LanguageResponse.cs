using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class LanguageResponse
{
    [JsonPropertyName("languageCode")]
    [DataMember(Name = "languageCode")]
    [Required]
    public string LanguageCode { get; set; } = null!;

    [JsonPropertyName("languageName")]
    [DataMember(Name = "languageName")]
    [Required]
    public string LanguageName { get; set; } = null!;
}
