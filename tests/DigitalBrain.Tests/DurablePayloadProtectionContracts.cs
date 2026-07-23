using System.Security.Cryptography;
using DigitalBrain.Security;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DurablePayloadProtectionContracts
{
    private const string Purpose = "maf/session/owner-17";

    [Fact]
    public void DurablePayloadSurvivesProtectorRecreationWithTheSameKey()
    {
        var key = NewEncodedKey();
        var first = new DurablePayloadProtector(key);
        var second = new DurablePayloadProtector(key);
        var plaintext = "durable state"u8.ToArray();

        var protectedPayload = first.Protect(Purpose, plaintext);

        Assert.Equal(plaintext, second.Unprotect(Purpose, protectedPayload));
        Assert.NotEqual(plaintext, protectedPayload);
    }

    [Fact]
    public void AChangedKeyCannotSilentlyRecoverDurablePayload()
    {
        var protectedPayload = new DurablePayloadProtector(NewEncodedKey())
            .Protect(Purpose, "durable state"u8);

        Assert.ThrowsAny<CryptographicException>(() =>
            new DurablePayloadProtector(NewEncodedKey()).Unprotect(Purpose, protectedPayload));
    }

    [Fact]
    public void AChangedPurposeCannotSilentlyRecoverDurablePayload()
    {
        var protector = new DurablePayloadProtector(NewEncodedKey());
        var protectedPayload = protector.Protect(Purpose, "durable state"u8);

        Assert.ThrowsAny<CryptographicException>(() =>
            protector.Unprotect("maf/session/another-owner", protectedPayload));
    }

    [Fact]
    public void TamperingCannotSilentlyRecoverDurablePayload()
    {
        var protector = new DurablePayloadProtector(NewEncodedKey());
        var protectedPayload = protector.Protect(Purpose, "durable state"u8);
        protectedPayload[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Purpose, protectedPayload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AA==")]
    public void ProtectionKeyMustBeBase64EncodedAndExactly256Bits(string encodedKey)
    {
        Assert.Throws<ArgumentException>(() => new DurablePayloadProtector(encodedKey));
    }

    private static string NewEncodedKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }
}
