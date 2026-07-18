using System.Net;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.Comms.Core.Helpers;

namespace TripRadar.Server.API.Security;

internal sealed class InternalAccessValidator(IConfiguration configuration) : IInternalAccessValidator
{
    private const string InternalApiKeyHeaderName = "X-Internal-Auth";
    private readonly HashSet<string> _internalApiKeys = GetInternalApiKeys(configuration);
    private readonly HashSet<string> _allowedIps = GetAllowedIps(configuration);

    public InternalAccessValidationResult Validate(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(InternalApiKeyHeaderName, out var extractedKey) ||
            extractedKey.Count != 1 ||
            string.IsNullOrWhiteSpace(extractedKey[0]))
        {
            return new InternalAccessValidationResult(false, true);
        }

        if (_internalApiKeys.Count == 0)
        {
            return new InternalAccessValidationResult(false, false);
        }

        var providedKey = extractedKey[0]!;
        var isKeyValid = _internalApiKeys.Any(key => ComparerHelper.Compare(key, providedKey));
        if (!isKeyValid)
        {
            return new InternalAccessValidationResult(false, false);
        }

        if (_allowedIps.Count == 0)
        {
            return new InternalAccessValidationResult(true, false);
        }

        var clientIp = NormalizeIp(httpContext.Connection.RemoteIpAddress?.ToString());
        var isIpAllowed = clientIp is not null && _allowedIps.Contains(clientIp);
        return new InternalAccessValidationResult(isIpAllowed, false);
    }

    private static HashSet<string> GetInternalApiKeys(IConfiguration configuration)
    {
        var rawKeys = configuration.GetValue<string>("INTERNAL_API_KEYS")
                     ?? configuration.GetValue<string>("InternalApiKeys")
                     ?? configuration.GetValue<string>("InternalApi:Keys");

        if (!string.IsNullOrWhiteSpace(rawKeys))
        {
            return rawKeys
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal);
        }

        var singleKey = configuration.GetValue<string>("INTERNAL_API_KEY")
                        ?? configuration.GetValue<string>("InternalApiKey")
                        ?? configuration.GetValue<string>("InternalApi:Key");

        return string.IsNullOrWhiteSpace(singleKey)
            ? []
            : new HashSet<string>([singleKey], StringComparer.Ordinal);
    }

    private static HashSet<string> GetAllowedIps(IConfiguration configuration)
    {
        var rawIps = configuration.GetValue<string>("INTERNAL_ALLOWED_IPS")
                     ?? configuration.GetValue<string>("InternalApiAllowedIps")
                     ?? configuration.GetValue<string>("InternalApi:AllowedIps");

        if (string.IsNullOrWhiteSpace(rawIps))
        {
            return [];
        }

        return rawIps
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeIp)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Select(ip => ip!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        return !IPAddress.TryParse(ip, out var parsed) ? ip.Trim() : (parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed).ToString();
    }
}
