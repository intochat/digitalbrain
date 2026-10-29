using DigitalBrain.Capabilities;
using System.Text.Json;
using DigitalBrain.Capabilities.Secrets;
using Xunit;

namespace DigitalBrain.Testing.Conformance.Secrets;

/// <summary>A connected secrets@1 implementation under test.</summary>
/// <param name="Secrets">The secrets connection.</param>
/// <param name="Callers">Switches the runtime caller the implementation sees.</param>
/// <param name="CapturedLogs">Every log line the implementation wrote so far; none may contain a secret value.</param>
public sealed record SecretsSubject(ISecrets Secrets, CallerScope Callers, Func<IReadOnlyList<string>> CapturedLogs)
{
    /// <summary>A deployment-provided secret one module declares; null skips the named-secret facts.</summary>
    public KitNamedSecret? Named { get; init; }
}

/// <summary>A deployment-provided secret for the named-secret facts.</summary>
/// <param name="ModuleId">A module whose manifest declares the secret (its <c>Secrets</c> list).</param>
/// <param name="Name">The configured name, for example <c>kit-issuer:SigningKey</c>; the part before ':' is the declared secret.</param>
/// <param name="Value">The configured value.</param>
public sealed record KitNamedSecret(string ModuleId, string Name, string Value);

/// <summary>
/// The secrets@1 conformance kit (CAP-01). A module providing secrets subclasses it in its <c>tests/</c> project as a sealed
/// class and implements <see cref="Connect"/>; every inherited fact runs against that subject. Concurrent sets of one name
/// are required to be last-writer-wins (no ETag in the v1 surface). Assertion messages carry names, never values.
/// </summary>
/// <typeparam name="TSecrets">The implementation under test (names the subclass's subject; not otherwise used).</typeparam>
public abstract class SecretsConformance<TSecrets> where TSecrets : ISecrets
{
    /// <summary>Every fact's timeout in milliseconds.</summary>
    protected const int Timeout = 30_000;

    /// <summary>Connects to the implementation. Called once per fact.</summary>
    protected abstract Task<SecretsSubject> Connect(CancellationToken ct);

    /// <summary>The caller that owns the secrets in each fact. Override when the implementation needs a provisioned brain.</summary>
    protected virtual CapabilityCaller Owner { get; } = new("kit-ada", "kit-ada-account", "kit-ada-brain");

    /// <summary>A caller in another brain; it must be denied the owner's secrets.</summary>
    protected virtual CapabilityCaller Stranger { get; } = new("kit-bob", "kit-bob-account", "kit-bob-brain");

    /// <summary>Set then Resolve returns the value.</summary>
    [Fact(Timeout = Timeout)]
    public async Task SetThenResolveReturnsValue()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var value = NewValue();
        using (s.Callers.As(Owner))
        {
            var secret = await s.Secrets.Set(NewName(), "Kit key", value, ct).ConfigureAwait(false);
            Assert.True(value == await s.Secrets.Resolve(secret, ct).ConfigureAwait(false), $"{secret.Reference} did not resolve to its value");
        }
    }

    /// <summary>Set returns a set reference with the label; neither ToString nor JSON of the reference carries the value.</summary>
    [Fact(Timeout = Timeout)]
    public async Task SetReturnsRefWithNameAndNoValue()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var value = NewValue();
        var name = NewName();
        using (s.Callers.As(Owner))
        {
            var secret = await s.Secrets.Set(name, "Kit label", value, ct).ConfigureAwait(false);
            Assert.True(secret.IsSet, $"{name} is not reported set");
            Assert.Equal("Kit label", secret.Label);
            Assert.False(string.IsNullOrWhiteSpace(secret.Reference), $"{name} has no reference");
            Assert.False(Leaks(secret.ToString(), value), $"{name}: ToString carries the value");
            Assert.False(Leaks(JsonSerializer.Serialize(secret), value), $"{name}: the JSON form carries the value");
            Assert.False(Leaks(secret.Reference, value) || Leaks(secret.Label, value), $"{name}: the reference carries the value");
        }
    }

    /// <summary>Remove then Resolve throws <see cref="SecretNotFound"/> with code not-found.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RemoveThenResolveIsNotFound()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var name = NewName();
        using (s.Callers.As(Owner))
        {
            var secret = await s.Secrets.Set(name, "Kit key", NewValue(), ct).ConfigureAwait(false);
            await s.Secrets.Remove(name, ct).ConfigureAwait(false);
            var error = await Assert.ThrowsAsync<SecretNotFound>(() => s.Secrets.Resolve(secret, ct)).ConfigureAwait(false);
            Assert.Equal("not-found", error.Code);
        }
    }

    /// <summary>Another brain's caller resolving the owner's reference throws <see cref="SecretDenied"/> with code denied.</summary>
    [Fact(Timeout = Timeout)]
    public async Task OtherCallersSecretIsDenied()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        SecretRef secret;
        using (s.Callers.As(Owner))
        {
            secret = await s.Secrets.Set(NewName(), "Kit key", NewValue(), ct).ConfigureAwait(false);
        }

        using (s.Callers.As(Stranger))
        {
            var error = await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Resolve(secret, ct)).ConfigureAwait(false);
            Assert.Equal("denied", error.Code);
        }
    }

    /// <summary>Setting a name twice keeps the latest value.</summary>
    [Fact(Timeout = Timeout)]
    public async Task OverwriteKeepsLatestValue()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var name = NewName();
        var latest = NewValue();
        using (s.Callers.As(Owner))
        {
            await s.Secrets.Set(name, "Kit key", NewValue(), ct).ConfigureAwait(false);
            var secret = await s.Secrets.Set(name, "Kit key", latest, ct).ConfigureAwait(false);
            Assert.True(latest == await s.Secrets.Resolve(secret, ct).ConfigureAwait(false), $"{name} did not keep the latest value");
        }
    }

    /// <summary>Concurrent sets of one name are last-writer-wins: the stored value is one of the written values, and a later set wins.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ConcurrentSetsAreLastWriterWins()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var name = NewName();
        var values = Enumerable.Range(0, 8).Select(_ => NewValue()).ToList();
        using (s.Callers.As(Owner))
        {
            var refs = await Task.WhenAll(values.Select(value => s.Secrets.Set(name, "Kit key", value, ct))).ConfigureAwait(false);
            var stored = await s.Secrets.Resolve(refs[0], ct).ConfigureAwait(false);
            Assert.True(values.Contains(stored), $"{name} holds a value no writer wrote");
            var last = NewValue();
            var secret = await s.Secrets.Set(name, "Kit key", last, ct).ConfigureAwait(false);
            Assert.True(last == await s.Secrets.Resolve(secret, ct).ConfigureAwait(false), $"{name}: the later set did not win");
        }
    }

    /// <summary>A 256 KiB value round-trips unchanged.</summary>
    [Fact(Timeout = Timeout)]
    public async Task LargeValueRoundTrips()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var value = string.Concat(Enumerable.Repeat(NewValue(), 1 + (256 * 1024 / 40)));
        using (s.Callers.As(Owner))
        {
            var secret = await s.Secrets.Set(NewName(), "Kit large", value, ct).ConfigureAwait(false);
            var resolved = await s.Secrets.Resolve(secret, ct).ConfigureAwait(false);
            Assert.True(value == resolved, $"{secret.Reference}: the large value changed ({value.Length} chars in, {resolved.Length} out)");
        }
    }

    /// <summary>Not-found and denied errors carry the name only: neither message nor ToString holds the value.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ErrorsNeverCarryTheValue()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var value = NewValue();
        var name = NewName();
        SecretRef secret;
        using (s.Callers.As(Owner))
        {
            secret = await s.Secrets.Set(name, "Kit key", value, ct).ConfigureAwait(false);
        }

        using (s.Callers.As(Stranger))
        {
            var denied = await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Resolve(secret, ct)).ConfigureAwait(false);
            Assert.False(Leaks(denied.ToString(), value), $"{name}: the denied error carries the value");
        }

        using (s.Callers.As(Owner))
        {
            await s.Secrets.Remove(name, ct).ConfigureAwait(false);
            var missing = await Assert.ThrowsAsync<SecretNotFound>(() => s.Secrets.Resolve(secret, ct)).ConfigureAwait(false);
            Assert.False(Leaks(missing.ToString(), value), $"{name}: the not-found error carries the value");
        }
    }

    /// <summary>After set, resolve, denied resolve and remove, no captured log line contains the value (T-05-05-01).</summary>
    [Fact(Timeout = Timeout)]
    public async Task LogsNeverContainValue()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var value = NewValue();
        var name = NewName();
        SecretRef secret;
        using (s.Callers.As(Owner))
        {
            secret = await s.Secrets.Set(name, "Kit key", value, ct).ConfigureAwait(false);
            await s.Secrets.Resolve(secret, ct).ConfigureAwait(false);
        }

        using (s.Callers.As(Stranger))
        {
            await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Resolve(secret, ct)).ConfigureAwait(false);
        }

        using (s.Callers.As(Owner))
        {
            await s.Secrets.Remove(name, ct).ConfigureAwait(false);
        }

        var lines = s.CapturedLogs();
        var leaking = lines.Count(line => Leaks(line, value));
        Assert.True(leaking == 0, $"{name}: {leaking} of {lines.Count} captured log lines contain the value");
    }

    // ----- sealed values (09-03, secrets row 1) -----

    /// <summary>Seal then Open with the same context returns the plaintext (a TOTP seed).</summary>
    [Fact(Timeout = Timeout)]
    public async Task SealThenOpenRoundTrips()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var seed = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var context = $"auth/totp/{NewName()}";
        using (s.Callers.As(Owner))
        {
            var sealedSecret = await s.Secrets.Seal(context, seed, ct).ConfigureAwait(false);
            Assert.Equal(seed, await s.Secrets.Open(context, sealedSecret, ct).ConfigureAwait(false));
            Assert.Empty(await s.Secrets.Open($"{context}/empty", await s.Secrets.Seal($"{context}/empty", ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
        }
    }

    /// <summary>Open with another context, or of a tampered value, throws <see cref="SecretDenied"/>.</summary>
    [Fact(Timeout = Timeout)]
    public async Task OpenWithOtherContextIsDenied()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var context = $"auth/totp/{NewName()}";
        using (s.Callers.As(Owner))
        {
            var sealedSecret = await s.Secrets.Seal(context, System.Security.Cryptography.RandomNumberGenerator.GetBytes(20), ct).ConfigureAwait(false);
            await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Open($"auth/totp/{NewName()}", sealedSecret, ct)).ConfigureAwait(false);
            var bytes = Convert.FromBase64String(sealedSecret.Cipher);
            bytes[^1] ^= 0x01;
            await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Open(context, sealedSecret with { Cipher = Convert.ToBase64String(bytes) }, ct)).ConfigureAwait(false);
            await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Open(context, sealedSecret with { KeyId = $"{sealedSecret.KeyId}-other" }, ct)).ConfigureAwait(false);
        }
    }

    /// <summary>The sealed form (and its JSON) holds no plaintext; sealing the same value twice gives different ciphers (T-09-10).</summary>
    [Fact(Timeout = Timeout)]
    public async Task SealedValueDoesNotContainPlaintext()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var plain = NewValue();
        var bytes = System.Text.Encoding.UTF8.GetBytes(plain);
        var context = $"auth/totp/{NewName()}";
        using (s.Callers.As(Owner))
        {
            var first = await s.Secrets.Seal(context, bytes, ct).ConfigureAwait(false);
            var second = await s.Secrets.Seal(context, bytes, ct).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(first);
            Assert.Contains("\"cipher\"", json, StringComparison.Ordinal);
            Assert.Contains("\"keyId\"", json, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(first.Cipher) || string.IsNullOrWhiteSpace(first.KeyId), "the sealed value has no cipher or key id");
            foreach (var form in new[] { plain, Convert.ToBase64String(bytes), Convert.ToHexString(bytes) })
            {
                Assert.False(Leaks(json, form) || Leaks(first.ToString(), form), $"{context}: the sealed value carries the plaintext");
            }

            Assert.NotEqual(first.Cipher, second.Cipher);
        }

        var leaking = s.CapturedLogs().Count(line => Leaks(line, plain));
        Assert.True(leaking == 0, $"{context}: {leaking} captured log lines contain the plaintext");
    }

    // ----- named secrets (09-03, secrets rows 2 and 3) -----

    /// <summary>A module declaring the secret gets a set reference that resolves to the configured value; an unconfigured key is unset.</summary>
    [Fact(Timeout = Timeout)]
    public async Task NamedDeclaredSecretResolves()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var named = RequireNamed(s);
        using (s.Callers.As(Owner with { AppId = named.ModuleId }))
        {
            var secret = await s.Secrets.Named(named.Name, ct).ConfigureAwait(false);
            Assert.True(secret.IsSet, $"{named.Name} is not reported set");
            Assert.True(secret.IsNamed, $"{named.Name}: {secret.Reference} is not a named reference");
            Assert.False(Leaks(secret.ToString(), named.Value) || Leaks(JsonSerializer.Serialize(secret), named.Value), $"{named.Name}: the reference carries the value");
            Assert.True(named.Value == await s.Secrets.Resolve(secret, ct).ConfigureAwait(false), $"{named.Name} did not resolve to its value");
            var unset = await s.Secrets.Named($"{Declared(named)}:KitUnconfigured", ct).ConfigureAwait(false);
            Assert.False(unset.IsSet, $"{Declared(named)}:KitUnconfigured is reported set");
            await Assert.ThrowsAsync<SecretNotFound>(() => s.Secrets.Resolve(unset, ct)).ConfigureAwait(false);
        }
    }

    /// <summary>An undeclared name, another module, or a caller that is not a module is denied; so is resolving the reference.</summary>
    [Fact(Timeout = Timeout)]
    public async Task NamedUndeclaredSecretIsDenied()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var named = RequireNamed(s);
        SecretRef secret;
        using (s.Callers.As(Owner with { AppId = named.ModuleId }))
        {
            secret = await s.Secrets.Named(named.Name, ct).ConfigureAwait(false);
            var undeclared = await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Named($"kit-undeclared-{Guid.NewGuid():N}", ct)).ConfigureAwait(false);
            Assert.Equal("denied", undeclared.Code);
        }

        foreach (var caller in new[] { Owner with { AppId = $"kit-other-{Guid.NewGuid():N}" }, Owner })
        {
            using (s.Callers.As(caller))
            {
                await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Named(named.Name, ct)).ConfigureAwait(false);
                var denied = await Assert.ThrowsAsync<SecretDenied>(() => s.Secrets.Resolve(secret, ct)).ConfigureAwait(false);
                Assert.False(Leaks(denied.ToString(), named.Value), $"{named.Name}: the denied error carries the value");
            }
        }
    }

    private static KitNamedSecret RequireNamed(SecretsSubject s)
    {
        Assert.SkipWhen(s.Named is null, "the subject declares no named secret");
        return s.Named!;
    }

    private static string Declared(KitNamedSecret named)
    {
        var colon = named.Name.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? named.Name : named.Name[..colon];
    }

    private async Task<(SecretsSubject Subject, CancellationToken Ct)> Open()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = await Connect(ct).ConfigureAwait(false);
        Assert.NotNull(subject);
        return (subject, ct);
    }

    private static string NewName() => $"kit-{Guid.NewGuid():N}";

    private static string NewValue() => $"v-{Guid.NewGuid():N}";

    private static bool Leaks(string? text, string value) => text is not null && text.Contains(value, StringComparison.Ordinal);
}
