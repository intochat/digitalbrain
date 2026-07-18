using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.API.Services;

public class GoogleIdTokenValidator(IOptions<GoogleAuth> googleAuthOptions) : IGoogleIdTokenValidator
{
    private readonly string[] _googleAudiences = SplitConfiguredValues(googleAuthOptions.Value.ClientId).ToArray();

    public async Task<GoogleJsonWebSignature.Payload?> ValidateAsync(string idToken)
    {
        if (!JwtExtensions.IsValidJwtFormat(idToken))
            return null;

        try
        {
            return await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = _googleAudiences });
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SplitConfiguredValues(string? configuredValue) =>
        (configuredValue ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(value => !value.Contains('{') && !value.Contains('}'));
}
