using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Google.Gmail;

internal sealed class GmailSecrets(IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("DigitalBrain.Google.Gmail.RefreshToken.v1");

    public string Protect(string refreshToken)
    {
        GmailTokenRefresh.ValidateToken(refreshToken);
        return _protector.Protect(refreshToken);
    }

    public string Unprotect(string protectedRefreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedRefreshToken);
        var refreshToken = _protector.Unprotect(protectedRefreshToken);
        GmailTokenRefresh.ValidateToken(refreshToken);
        return refreshToken;
    }
}
