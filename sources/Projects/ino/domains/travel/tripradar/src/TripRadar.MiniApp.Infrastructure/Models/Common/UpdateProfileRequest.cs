using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record UpdateProfileRequest(
        [property: JsonPropertyName("firstName")] string? FirstName = null,
        [property: JsonPropertyName("lastName")] string? LastName = null,
        [property: JsonPropertyName("phoneNumber")] string? PhoneNumber = null,
        [property: JsonPropertyName("languageCode")] string? LanguageCode = null,
        [property: JsonPropertyName("countryCode")] string? CountryCode = null,
        [property: JsonPropertyName("allowsMarketingEmails")] bool? AllowsMarketingEmails = null
    );
}