using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class UpdateUserProfileRequest
{
    [JsonPropertyName("firstName")]
    [DataMember(Name = "firstName")]
    [MaxLength(255)]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    [DataMember(Name = "lastName")]
    [MaxLength(255)]
    public string? LastName { get; set; }

    [JsonPropertyName("phoneNumber")]
    [DataMember(Name = "phoneNumber")]
    [MaxLength(255)]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("timezoneId")]
    [DataMember(Name = "timezoneId")]
    [Range(1, int.MaxValue)]
    public int? TimezoneId { get; set; }

    [JsonPropertyName("profilePictureUrl")]
    [DataMember(Name = "profilePictureUrl")]
    [MaxLength(500)]
    [Url(ErrorMessage = "Profile picture URL must be a valid URL")]
    public string? ProfilePictureUrl { get; set; }

    [JsonPropertyName("languageCode")]
    [DataMember(Name = "languageCode")]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "Language code must be between 2 and 10 characters")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("countryCode")]
    [DataMember(Name = "countryCode")]
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Country code must be exactly 2 characters")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("allowsMarketingEmails")]
    [DataMember(Name = "allowsMarketingEmails")]
    public bool? AllowsMarketingEmails { get; set; }
}
