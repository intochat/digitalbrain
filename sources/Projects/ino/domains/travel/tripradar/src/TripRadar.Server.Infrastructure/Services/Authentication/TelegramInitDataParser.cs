using System.Globalization;
using System.Net;
using System.Text.Json;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Infrastructure.Contracts.Authentication;

namespace TripRadar.Server.Infrastructure.Services.Authentication;

internal sealed class TelegramInitDataParser : ITelegramInitDataParser
{
    public bool TryParse(string initData, out TelegramAuthDataDTO authData)
    {
        authData = null!;
        if (!TryParseInitData(initData, out var queryData))
            return false;

        var hash = GetSingleValue(queryData, "hash");
        var authDateValue = GetSingleValue(queryData, "auth_date");
        if (string.IsNullOrWhiteSpace(hash) ||
            string.IsNullOrWhiteSpace(authDateValue) ||
            !long.TryParse(authDateValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authDate))
        {
            return false;
        }

        var userJson = GetSingleValue(queryData, "user");
        if (!string.IsNullOrWhiteSpace(userJson))
        {
            if (!TryParseMiniAppUser(userJson, out var id, out var firstName, out var lastName, out var username, out var photoUrl))
                return false;

            authData = new TelegramAuthDataDTO
            {
                Id = id,
                FirstName = firstName,
                LastName = lastName,
                Username = username,
                PhotoUrl = photoUrl,
                AuthDate = authDate,
                Hash = hash,
                RawInitData = initData
            };

            return true;
        }

        var idValue = GetSingleValue(queryData, "id");
        var firstNameValue = GetSingleValue(queryData, "first_name");
        if (string.IsNullOrWhiteSpace(idValue) ||
            string.IsNullOrWhiteSpace(firstNameValue) ||
            !long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyId))
        {
            return false;
        }

        authData = new TelegramAuthDataDTO
        {
            Id = legacyId,
            FirstName = firstNameValue,
            LastName = GetSingleValue(queryData, "last_name"),
            Username = GetSingleValue(queryData, "username"),
            PhotoUrl = GetSingleValue(queryData, "photo_url"),
            AuthDate = authDate,
            Hash = hash
        };

        return true;
    }

    private static bool TryParseInitData(string initData, out Dictionary<string, string> queryData)
    {
        queryData = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(initData))
            return false;

        var pairs = initData.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pairs.Length == 0)
            return false;

        foreach (var pair in pairs)
        {
            var separatorIndex = pair.IndexOf('=');
            var rawKey = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            var rawValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;

            var key = WebUtility.UrlDecode(rawKey);
            var value = WebUtility.UrlDecode(rawValue);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            // Duplicated keys are ambiguous for Telegram auth validation.
            if (!queryData.TryAdd(key, value))
                return false;
        }

        return true;
    }

    private static string? GetSingleValue(
        IReadOnlyDictionary<string, string> queryData,
        string key) =>
        queryData.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool TryParseMiniAppUser(
        string userJson,
        out long id,
        out string firstName,
        out string? lastName,
        out string? username,
        out string? photoUrl)
    {
        id = 0;
        firstName = string.Empty;
        lastName = null;
        username = null;
        photoUrl = null;

        try
        {
            using var document = JsonDocument.Parse(userJson);
            var userElement = document.RootElement;
            if (userElement.ValueKind != JsonValueKind.Object ||
                !userElement.TryGetProperty("id", out var idElement) ||
                !idElement.TryGetInt64(out id) ||
                id <= 0 ||
                !userElement.TryGetProperty("first_name", out var firstNameElement))
            {
                return false;
            }

            firstName = firstNameElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(firstName))
                return false;

            lastName = GetOptionalUserField(userElement, "last_name");
            username = GetOptionalUserField(userElement, "username");
            photoUrl = GetOptionalUserField(userElement, "photo_url");

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetOptionalUserField(JsonElement userElement, string fieldName) =>
        userElement.TryGetProperty(fieldName, out var fieldValue) &&
        fieldValue.ValueKind == JsonValueKind.String
            ? fieldValue.GetString()
            : null;
}
