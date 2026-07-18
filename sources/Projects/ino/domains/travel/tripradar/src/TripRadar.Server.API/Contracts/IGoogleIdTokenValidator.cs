using Google.Apis.Auth;

namespace TripRadar.Server.API.Contracts;

public interface IGoogleIdTokenValidator
{
    Task<GoogleJsonWebSignature.Payload?> ValidateAsync(string idToken);
}
