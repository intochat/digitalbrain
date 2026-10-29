using DigitalBrain.Capabilities;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Capabilities.Secrets.Tests.Unit;

public sealed class SecretsCapabilityFacts
{
    [Fact]
    public void Secrets_is_secrets_v1()
    {
        var attribute = typeof(ISecrets).GetCustomAttribute<CapabilityAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(Capability.Secrets.V1, attribute.Id);
    }

    // D-15: the caller comes from the runtime context, never from a parameter.
    [Fact]
    public void No_member_takes_a_caller()
    {
        var parameters = typeof(ISecrets).GetMethods().SelectMany(method => method.GetParameters()).ToList();
        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType.Name.Contains("Caller", StringComparison.Ordinal));
    }

    [Fact]
    public void Members_are_set_remove_resolve_seal_open_named()
    {
        Assert.Equal(["Named", "Open", "Remove", "Resolve", "Seal", "Set"], typeof(ISecrets).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(Task<SecretRef>), typeof(ISecrets).GetMethod("Set")!.ReturnType);
        Assert.Equal(typeof(Task<string>), typeof(ISecrets).GetMethod("Resolve")!.ReturnType);
        Assert.Equal(typeof(SecretRef), typeof(ISecrets).GetMethod("Resolve")!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public async Task Resolve_returns_the_value_set_and_remove_makes_it_not_found()
    {
        ISecrets secrets = new MemorySecrets();
        var reference = await secrets.Set("github", "GitHub token", "s3cr3t-value", TestContext.Current.CancellationToken);
        Assert.True(reference.IsSet);
        Assert.DoesNotContain("s3cr3t-value", reference.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-value", JsonSerializer.Serialize(reference), StringComparison.Ordinal);
        Assert.Equal("s3cr3t-value", await secrets.Resolve(reference, TestContext.Current.CancellationToken));

        await secrets.Remove("github", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<SecretNotFound>(() => secrets.Resolve(reference, TestContext.Current.CancellationToken));
        Assert.Equal("github", error.Name);
    }

    [Fact]
    public async Task Resolving_an_unset_reference_is_not_found()
    {
        ISecrets secrets = new MemorySecrets();
        await Assert.ThrowsAsync<SecretNotFound>(() => secrets.Resolve(SecretRef.Unset("github", "GitHub token"), TestContext.Current.CancellationToken));
    }

    private sealed class MemorySecrets : ISecrets
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public Task<SecretRef> Set(string name, string label, string value, CancellationToken cancellationToken = default)
        {
            values[name] = value;
            return Task.FromResult(new SecretRef { Reference = name, Label = label, Status = SecretStatus.Set });
        }

        public Task Remove(string name, CancellationToken cancellationToken = default)
        {
            values.Remove(name);
            return Task.CompletedTask;
        }

        public Task<string> Resolve(SecretRef secret, CancellationToken cancellationToken = default) =>
            secret.IsSet && values.TryGetValue(secret.Reference, out var value)
                ? Task.FromResult(value)
                : Task.FromException<string>(new SecretNotFound(secret.Reference));

        public Task<SealedSecret> Seal(string context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<byte[]> Open(string context, SealedSecret sealedSecret, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SecretRef> Named(string name, CancellationToken cancellationToken = default) => throw new SecretDenied(name);
    }
}

// 09-03: the sealed form and named references.
public sealed class SealedSecretFacts
{
    [Fact]
    public void Sealed_secret_json_is_cipher_and_key_id()
    {
        var sealedSecret = new SealedSecret { Cipher = "AAAA", KeyId = "k1" };
        var json = JsonSerializer.Serialize(sealedSecret);
        Assert.Equal("""{"cipher":"AAAA","keyId":"k1"}""", json);
        Assert.Equal(sealedSecret, JsonSerializer.Deserialize<SealedSecret>(json));
    }

    [Fact]
    public void Named_reference_has_the_named_prefix_and_no_extra_json()
    {
        var named = SecretRef.Unset(SecretRef.NamedPrefix + "signin-issuer:SigningKey", "signin-issuer:SigningKey");
        Assert.True(named.IsNamed);
        Assert.False(new SecretRef { Reference = "brain/github" }.IsNamed);
        Assert.DoesNotContain("isNamed", JsonSerializer.Serialize(named), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Port_defaults_refuse()
    {
        ISecretVaultPort port = new LegacyPort();
        var caller = new CapabilityCaller("p", "a", "b", "auth");
        var ct = TestContext.Current.CancellationToken;
        Assert.Null(await port.Seal(caller, "auth/totp/p", new byte[] { 1 }, ct));
        Assert.Null(await port.Open(caller, "auth/totp/p", new SealedSecret { Cipher = "AAAA", KeyId = "k" }, ct));
        Assert.Equal(SecretVaultStatus.Denied, (await port.Named(caller, "signin-issuer", ct)).Status);
    }

    // A port written before 09-03: only Set, Remove and Resolve.
    private sealed class LegacyPort : ISecretVaultPort
    {
        public Task<SecretVaultResult> Set(CapabilityCaller caller, string name, string label, string value, CancellationToken cancellationToken) => Task.FromResult(SecretVaultResult.Denied);

        public Task<SecretVaultStatus> Remove(CapabilityCaller caller, string name, CancellationToken cancellationToken) => Task.FromResult(SecretVaultStatus.Denied);

        public Task<SecretVaultResult> Resolve(CapabilityCaller caller, SecretRef secret, CancellationToken cancellationToken) => Task.FromResult(SecretVaultResult.Denied);
    }
}

public sealed class SecretRefFacts
{
    [Fact]
    public void Round_trips_through_json()
    {
        var reference = new SecretRef { Reference = "github", Label = "GitHub token", Status = SecretStatus.Set };
        var json = JsonSerializer.Serialize(reference);
        Assert.Equal(reference, JsonSerializer.Deserialize<SecretRef>(json));
        Assert.Contains("\"reference\":\"github\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isSet", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_is_unset()
    {
        var reference = new SecretRef();
        Assert.False(reference.IsSet);
        Assert.Equal("", reference.Reference);
    }

    [Fact]
    public void Unset_factory_keeps_name_and_label()
    {
        var reference = SecretRef.Unset("github", "GitHub token");
        Assert.Equal(SecretStatus.Unset, reference.Status);
        Assert.Equal("github", reference.Reference);
        Assert.Equal("GitHub token", reference.Label);
    }

    [Fact]
    public void Carries_no_value_property()
    {
        Assert.Equal(["IsNamed", "IsSet", "Label", "Reference", "Status"], typeof(SecretRef).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ToString_names_the_reference_only()
    {
        Assert.Equal("secret github (set)", new SecretRef { Reference = "github", Label = "GitHub token", Status = SecretStatus.Set }.ToString());
        Assert.Equal("secret github (unset)", SecretRef.Unset("github", "x").ToString());
    }
}

public sealed class SecretErrorFacts
{
    [Fact]
    public void Errors_carry_the_name_never_a_value()
    {
        var notFound = new SecretNotFound("github");
        var denied = new SecretDenied("github");
        Assert.Equal("secret not-found: github", notFound.Message);
        Assert.Equal("secret denied: github", denied.Message);
        Assert.Equal("not-found", notFound.Code);
        Assert.Equal("denied", denied.Code);
        Assert.IsAssignableFrom<SecretException>(denied);
    }

    [Fact]
    public void Blank_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SecretNotFound(" "));
    }
}
