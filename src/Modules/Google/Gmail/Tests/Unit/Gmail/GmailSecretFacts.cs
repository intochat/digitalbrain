using DigitalBrain.Google.Gmail;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GmailSecretFacts
{
    [Fact]
    public void RefreshTokenIsNotStoredInTheClear()
    {
        var secrets = new GmailSecrets(new EphemeralDataProtectionProvider());
        var protectedToken = secrets.Protect("refresh-token");
        Assert.DoesNotContain("refresh-token", protectedToken, StringComparison.Ordinal);
        Assert.Equal("refresh-token", secrets.Unprotect(protectedToken));
    }
}