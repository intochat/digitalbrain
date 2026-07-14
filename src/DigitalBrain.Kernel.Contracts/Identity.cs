using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Orleans;
namespace DigitalBrain.Kernel.Contracts;

[GenerateSerializer, Alias("digitalbrain.v3.brain-owner-id")]
public readonly record struct BrainOwnerId
{
    [JsonConstructor]
    public BrainOwnerId(string value) => Value = ContractValue.Identifier(value, nameof(value));
    public static BrainOwnerId FromExternalIdentity(string issuer, string subject) =>
        new(DerivedIdentity.Create("owner", issuer, subject));
    [Id(0)]
    public string Value { get; }
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v3.actor-id")]
public readonly record struct ActorId
{
    [JsonConstructor]
    public ActorId(string value) => Value = ContractValue.Identifier(value, nameof(value));
    public static ActorId FromExternalIdentity(string issuer, string subject) =>
        new(DerivedIdentity.Create("actor", issuer, subject));
    [Id(0)]
    public string Value { get; }
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v3.provider-connection-id")]
public readonly record struct ProviderConnectionId
{
    [JsonConstructor]
    public ProviderConnectionId(string value) => Value = ContractValue.Identifier(value, nameof(value));
    [Id(0)]
    public string Value { get; }
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v3.session-id")]
public readonly record struct SessionId
{
    [JsonConstructor]
    public SessionId(string value) => Value = ContractValue.Identifier(value, nameof(value));
    [Id(0)]
    public string Value { get; }
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-id")]
public readonly record struct FeatureInstallationId
{
    [JsonConstructor]
    public FeatureInstallationId(string value) => Value = ContractValue.Identifier(value, nameof(value));
    [Id(0)]
    public string Value { get; }
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v3.release-digest")]
public readonly record struct ReleaseDigest
{
    [JsonConstructor]
    public ReleaseDigest(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 release digest is required.", nameof(value));
        Value = value.ToLowerInvariant();
    }
    [Id(0)]
    public string Value { get; }
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v3.grant-revision")]
public readonly record struct GrantRevision
{
    [JsonConstructor]
    public GrantRevision(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }
    [Id(0)]
    public long Value { get; }
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
internal static class ContractValue
{
    public const int MaximumIdentifierLength = 256;
    public static string Identifier(string value, string parameterName) =>
        Text(value, parameterName, MaximumIdentifierLength);
    public static string Text(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(char.IsControl) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical identifier is required.", parameterName);
        return value;
    }
}
internal static class DerivedIdentity
{
    public static string Create(string domain, string issuer, string subject)
    {
        issuer = ContractValue.Text(issuer, nameof(issuer), 512);
        subject = ContractValue.Identifier(subject, nameof(subject));
        var canonical = Encoding.UTF8.GetBytes($"digitalbrain.v3.{domain}\0{issuer.Length}:{issuer}{subject.Length}:{subject}");
        return Convert.ToHexStringLower(SHA256.HashData(canonical));
    }
}
