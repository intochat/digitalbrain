using TripRadar.Server.Application.Contracts.Services.Authentication;

namespace TripRadar.Server.Infrastructure.Services;

public class CredentialValidator : ICredentialValidator
{
    public bool IsValidTokenFormat(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32) return false;

        const int maxStackAllocSize = 256;
        var buffer = token.Length <= maxStackAllocSize ? stackalloc byte[token.Length] : new byte[token.Length];
        return Convert.TryFromBase64String(token, buffer, out _);
    }

    public bool IsEmail(string input) => !string.IsNullOrWhiteSpace(input) && input.Contains('@') && input.IndexOf('@') > 0 && input.IndexOf('@') < input.Length - 1;
}
