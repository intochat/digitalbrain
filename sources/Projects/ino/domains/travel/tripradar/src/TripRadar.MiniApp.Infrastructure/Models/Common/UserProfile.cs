using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common;

public sealed record UserProfile(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("isEmailConfirmed")] bool IsEmailConfirmed,
    [property: JsonPropertyName("firstName")] string? FirstName,
    [property: JsonPropertyName("lastName")] string? LastName,
    [property: JsonPropertyName("phoneNumber")] string? PhoneNumber,
    [property: JsonPropertyName("telegramUserId")] long? TelegramUserId,
    [property: JsonPropertyName("timezoneId")] int TimezoneId,
    [property: JsonPropertyName("profilePictureUrl")] string? ProfilePictureUrl,
    [property: JsonPropertyName("languageCode")] string? LanguageCode,
    [property: JsonPropertyName("languageName")] string? LanguageName,
    [property: JsonPropertyName("countryCode")] string? CountryCode,
    [property: JsonPropertyName("countryName")] string? CountryName,
    [property: JsonPropertyName("allowsMarketingEmails")] bool AllowsMarketingEmails,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("tierName")] string TierName,
    [property: JsonPropertyName("createdOn")] DateTime CreatedOn
)
{
    public string DisplayName => FirstName is not null ? $"{FirstName} {LastName}".Trim() : Username;
}