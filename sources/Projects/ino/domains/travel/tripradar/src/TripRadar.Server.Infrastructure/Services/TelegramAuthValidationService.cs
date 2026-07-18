using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

/// <summary>
/// Service for validating Telegram Login Widget authentication data using HMAC-SHA256.
/// Implements the validation algorithm specified in Telegram's official documentation.
/// </summary>
public class TelegramAuthValidationService(IOptions<TelegramSettings> telegramSettings) : ITelegramAuthValidationService
{
    private readonly TelegramSettings _telegramSettings = telegramSettings.Value;
    private const int MaxAuthDateAgeSeconds = 300; // 5 minutes
    private const string HashFieldName = "hash";
    private const string AuthDateFieldName = "auth_date";
    private const string MiniAppSecretKey = "WebAppData";

    public bool Validate(TelegramAuthDataDTO authData)
    {
        if (string.IsNullOrWhiteSpace(_telegramSettings.BotToken))
        {
            throw new InvalidOperationException("Telegram bot token is not configured.");
        }

        if (string.IsNullOrWhiteSpace(authData.Hash))
            return false;

        return string.IsNullOrWhiteSpace(authData.RawInitData)
            ? ValidateLoginWidgetData(authData)
            : ValidateMiniAppData(authData);
    }

    private bool ValidateLoginWidgetData(TelegramAuthDataDTO authData)
    {
        if (!IsAuthDateValid(authData.AuthDate))
            return false;

        // Create data-check string from all received fields except hash
        var dataCheckString = BuildDataCheckString(authData);

        // Calculate the expected hash
        var expectedHash = CalculateLoginWidgetHash(dataCheckString, _telegramSettings.BotToken);

        return AreHexHashesEqual(expectedHash, authData.Hash);
    }

    private bool ValidateMiniAppData(TelegramAuthDataDTO authData)
    {
        if (!TryBuildMiniAppValidationContext(authData.RawInitData!, out var dataCheckString, out var authDate, out var actualHash))
            return false;

        if (!IsAuthDateValid(authDate))
            return false;

        // Protect against inconsistencies if caller provided both fields.
        if (!string.Equals(actualHash, authData.Hash, StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedHash = CalculateMiniAppHash(dataCheckString, _telegramSettings.BotToken);
        return AreHexHashesEqual(expectedHash, actualHash);
    }

    private static bool TryBuildMiniAppValidationContext(
        string rawInitData,
        out string dataCheckString,
        out long authDate,
        out string hash)
    {
        dataCheckString = string.Empty;
        authDate = 0;
        hash = string.Empty;

        if (string.IsNullOrWhiteSpace(rawInitData))
            return false;

        var queryData = QueryHelpers.ParseQuery(rawInitData);
        if (queryData.Count == 0)
            return false;

        if (queryData.Any(pair => pair.Value.Count != 1))
            return false;

        hash = GetSingleQueryValue(queryData, HashFieldName) ?? string.Empty;
        var authDateValue = GetSingleQueryValue(queryData, AuthDateFieldName);

        if (string.IsNullOrWhiteSpace(hash) ||
            string.IsNullOrWhiteSpace(authDateValue) ||
            !long.TryParse(authDateValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out authDate))
        {
            return false;
        }

        var dataCheckParts = queryData
            .Where(pair => !string.Equals(pair.Key, HashFieldName, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value[0]}")
            .ToArray();

        if (dataCheckParts.Length == 0)
            return false;

        dataCheckString = string.Join('\n', dataCheckParts);
        return true;
    }

    private static string? GetSingleQueryValue(
        IReadOnlyDictionary<string, StringValues> queryData,
        string key)
    {
        if (!queryData.TryGetValue(key, out var values) || values.Count != 1)
            return null;

        var value = values[0];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool AreHexHashesEqual(string expectedHash, string actualHash)
    {
        byte[] expectedHashBytes;
        byte[] actualHashBytes;

        try
        {
            expectedHashBytes = Convert.FromHexString(expectedHash);
            actualHashBytes = Convert.FromHexString(actualHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedHashBytes, actualHashBytes);
    }

    /// <summary>
    /// Validates that the auth_date is within the allowed time window.
    /// </summary>
    /// <param name="authDate">Unix timestamp of the authentication.</param>
    /// <param name="maxAgeSeconds">Maximum age in seconds (defaults to 5 minutes).</param>
    /// <returns>True if auth_date is valid and recent; otherwise, false.</returns>
    private static bool IsAuthDateValid(long authDate, int maxAgeSeconds = MaxAuthDateAgeSeconds)
    {
        var authDateTime = DateTimeOffset.FromUnixTimeSeconds(authDate);
        var now = DateTimeOffset.UtcNow;
        var age = now - authDateTime;

        // Reject if auth_date is in the future (clock skew tolerance of 60 seconds)
        if (age.TotalSeconds < -60)
        {
            return false;
        }

        // Reject if auth_date is too old
        return age.TotalSeconds <= maxAgeSeconds;
    }

    /// <summary>
    /// Builds the data-check string from Telegram auth data fields.
    /// Fields are sorted alphabetically and formatted as key=value pairs separated by newlines.
    /// </summary>
    private static string BuildDataCheckString(TelegramAuthDataDTO authData)
    {
        var dataFields = new SortedDictionary<string, string>
        {
            { "auth_date", authData.AuthDate.ToString(CultureInfo.InvariantCulture) },
            { "first_name", authData.FirstName },
            { "id", authData.Id.ToString(CultureInfo.InvariantCulture) }
        };

        // Add optional fields only if they are present
        if (!string.IsNullOrWhiteSpace(authData.LastName))
        {
            dataFields.Add("last_name", authData.LastName);
        }

        if (!string.IsNullOrWhiteSpace(authData.Username))
        {
            dataFields.Add("username", authData.Username);
        }

        if (!string.IsNullOrWhiteSpace(authData.PhotoUrl))
        {
            dataFields.Add("photo_url", authData.PhotoUrl);
        }

        // Build the data-check string
        var dataCheckParts = dataFields.Select(kvp => $"{kvp.Key}={kvp.Value}");
        return string.Join("\n", dataCheckParts);
    }

    /// <summary>
    /// Calculates the Telegram Login Widget hash.
    /// Uses SHA256(bot_token) as HMAC key.
    /// </summary>
    private static string CalculateLoginWidgetHash(string dataCheckString, string botToken)
    {
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        return CalculateHmacHex(dataCheckString, secretKey);
    }

    /// <summary>
    /// Calculates the Telegram Mini App hash for initData.
    /// Uses HMAC_SHA256(bot_token, "WebAppData") as secret.
    /// </summary>
    private static string CalculateMiniAppHash(string dataCheckString, string botToken)
    {
        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes(MiniAppSecretKey), Encoding.UTF8.GetBytes(botToken));
        return CalculateHmacHex(dataCheckString, secretKey);
    }

    private static string CalculateHmacHex(string dataCheckString, byte[] secretKey)
    {
        var dataBytes = Encoding.UTF8.GetBytes(dataCheckString);
        var hashBytes = HMACSHA256.HashData(secretKey, dataBytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
