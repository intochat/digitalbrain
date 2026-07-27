using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Security;

internal static class DurablePayloadProtectionHosting
{
    private const string ConfigurationKey = "DigitalBrain:Security:StateProtectionKey";

    internal static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var protector = new DurablePayloadProtector(
            configuration[ConfigurationKey]
            ?? throw new InvalidOperationException(
                $"Missing shared durable state-protection key '{ConfigurationKey}'."));
        services.TryAddSingleton<IDurablePayloadProtector>(protector);
    }
}

file sealed class DurablePayloadProtector : IDurablePayloadProtector
{
    private const byte EnvelopeVersion = 1;
    private const int MasterKeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 1 + NonceLength + TagLength;
    private static readonly byte[] AssociatedDataPrefix = "DigitalBrain.Security/v1/"u8.ToArray();
    private readonly byte[] masterKey;

    internal DurablePayloadProtector(string encodedKey)
    {
        ArgumentNullException.ThrowIfNull(encodedKey);

        try
        {
            masterKey = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The durable state-protection key must be base64 encoded.", nameof(encodedKey), exception);
        }

        if (masterKey.Length != MasterKeyLength)
        {
            throw new ArgumentException("The durable state-protection key must contain exactly 256 bits.", nameof(encodedKey));
        }
    }

    public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
    {
        var purposeBytes = GetPurposeBytes(purpose);
        var derivedKey = HMACSHA256.HashData(masterKey, purposeBytes);
        var protectedPayload = new byte[HeaderLength + plaintext.Length];
        protectedPayload[0] = EnvelopeVersion;

        var nonce = protectedPayload.AsSpan(1, NonceLength);
        var tag = protectedPayload.AsSpan(1 + NonceLength, TagLength);
        var ciphertext = protectedPayload.AsSpan(HeaderLength);
        RandomNumberGenerator.Fill(nonce);

        try
        {
            using var encryption = new AesGcm(derivedKey, TagLength);
            encryption.Encrypt(nonce, plaintext, ciphertext, tag, purposeBytes);
            return protectedPayload;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(purposeBytes);
        }
    }

    public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload)
    {
        if (protectedPayload.Length < HeaderLength || protectedPayload[0] != EnvelopeVersion)
        {
            throw new CryptographicException("The durable payload envelope is invalid or unsupported.");
        }

        var purposeBytes = GetPurposeBytes(purpose);
        var derivedKey = HMACSHA256.HashData(masterKey, purposeBytes);
        var plaintext = new byte[protectedPayload.Length - HeaderLength];

        try
        {
            using var encryption = new AesGcm(derivedKey, TagLength);
            encryption.Decrypt(
                protectedPayload.Slice(1, NonceLength),
                protectedPayload.Slice(HeaderLength),
                protectedPayload.Slice(1 + NonceLength, TagLength),
                plaintext,
                purposeBytes);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(purposeBytes);
        }
    }

    private static byte[] GetPurposeBytes(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return [.. AssociatedDataPrefix, .. Encoding.UTF8.GetBytes(purpose)];
    }
}
