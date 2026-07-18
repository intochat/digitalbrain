using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Update;

public class UpdateUserProfileResponse
{
    [JsonPropertyName("username")]
    [DataMember(Name = "username")]
    [Required]
    public string Username { get; set; } = null!;

    [JsonPropertyName("email")]
    [DataMember(Name = "email")]
    [Required]
    public string Email { get; set; } = null!;

    [JsonPropertyName("isEmailConfirmed")]
    [DataMember(Name = "isEmailConfirmed")]
    [Required]
    public bool IsEmailConfirmed { get; set; }

    [JsonPropertyName("firstName")]
    [DataMember(Name = "firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    [DataMember(Name = "lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("phoneNumber")]
    [DataMember(Name = "phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("googleId")]
    [DataMember(Name = "googleId")]
    public string? GoogleId { get; set; }

    [JsonPropertyName("timezoneId")]
    [DataMember(Name = "timezoneId")]
    [Required]
    public int TimezoneId { get; set; }

    [JsonPropertyName("profilePictureUrl")]
    [DataMember(Name = "profilePictureUrl")]
    public string? ProfilePictureUrl { get; set; }

    [JsonPropertyName("languageCode")]
    [DataMember(Name = "languageCode")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("languageName")]
    [DataMember(Name = "languageName")]
    public string? LanguageName { get; set; }

    [JsonPropertyName("countryCode")]
    [DataMember(Name = "countryCode")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("countryName")]
    [DataMember(Name = "countryName")]
    public string? CountryName { get; set; }

    [JsonPropertyName("allowsMarketingEmails")]
    [DataMember(Name = "allowsMarketingEmails")]
    [Required]
    public bool AllowsMarketingEmails { get; set; }

    [JsonPropertyName("isActive")]
    [DataMember(Name = "isActive")]
    [Required]
    public bool IsActive { get; set; }

    [JsonPropertyName("tierName")]
    [DataMember(Name = "tierName")]
    [Required]
    public string TierName { get; set; } = null!;
}
