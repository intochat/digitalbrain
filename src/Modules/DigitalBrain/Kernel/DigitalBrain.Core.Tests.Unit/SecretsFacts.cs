using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Platform.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Xunit;

namespace DigitalBrain.Core.Tests.Unit;

public sealed class SecretsFacts
{
    private const string Owner = "owner-1";
    private const string Canary = "canary-secret-7f3a91";

    [Fact]
    public void StoresCiphertextAndResolvesByReference()
    {
        var state = new SecretsState { Owner = Owner };
        var store = new SecretsStore(new KeyVaultKeyWrapper(new FakeKeyVault()));

        var reference = store.Set(state, "api.key", "API key", Canary);

        Assert.DoesNotContain(Canary, state.Fields["api.key"].SealedSecret, StringComparison.Ordinal);
        Assert.Equal(Owner, reference.Owner);
        Assert.Equal(Canary, store.Resolve(state, reference));
    }

    [Fact]
    public void RejectsReferenceForAnotherOwner()
    {
        var state = new SecretsState { Owner = Owner };
        var store = new SecretsStore(new KeyVaultKeyWrapper(new FakeKeyVault()));
        store.Set(state, "api.key", "API key", Canary);

        Assert.Throws<InvalidOperationException>(() => store.Resolve(state, DigitalBrain.Contracts.Types.SecretRef.For("other", "api.key", "API key", true)));
    }

    [Fact]
    public void The_master_key_store_resolves_a_secret_it_has_set()
    {
        var state = new SecretsState { Owner = Owner };
        var store = MasterKeyStore("first master key");
        var reference = store.Set(state, "api.key", "API key", Canary);
        Assert.Equal(Canary, store.Resolve(state, reference));
    }

    [Fact]
    public void A_store_with_a_different_master_key_cannot_resolve_the_same_state()
    {
        var state = new SecretsState { Owner = Owner };
        var reference = MasterKeyStore("first master key").Set(state, "api.key", "API key", Canary);
        Assert.ThrowsAny<CryptographicException>(() => MasterKeyStore("different master key").Resolve(state, reference));
    }

    private static SecretsStore MasterKeyStore(string masterKey)
        => new(new MasterKeyWrapper(Options.Create(new MasterKeyOptions { MasterKey = masterKey })));

    [Fact]
    public void A_legacy_two_key_credential_requires_the_owner_to_re_enter_it()
    {
        var state = new SecretsState { Owner = Owner, WrappedOwnerKey = "dp2:old-owner-key" };
        var record = new SecretRecord
        {
            IsSecret = true,
            IsSet = true,
            SealedCredentialKey = "legacy-sealed-credential-key",
            SealedSecret = "legacy-sealed-secret",
        };
        state.Fields["api.key"] = record;
        var reference = DigitalBrain.Contracts.Types.SecretRef.For(Owner, "api.key", "API key", true);
        var error = Assert.Throws<InvalidOperationException>(() => MasterKeyStore("first master key").Resolve(state, reference));
        Assert.Contains("predates the master-key store", error.Message, StringComparison.Ordinal);
        Assert.Contains("re-enter", error.Message, StringComparison.Ordinal);
        Assert.Equal("legacy-sealed-credential-key", record.SealedCredentialKey);
        Assert.Equal("legacy-sealed-secret", record.SealedSecret);
    }

    [Fact]
    public async Task UserCannotResolveThroughGrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<SecretsModule>().StartAsync(ct);
        var secrets = brain.Get<ISecrets>(Owner);
        var reference = await secrets.Set(UserCaller(), "api.key", "API key", Canary, ct);

        await Assert.ThrowsAsync<UntrustedCallerException>(() => secrets.Resolve(UserCaller(), reference, ct));
        Assert.Equal(Canary, await secrets.Resolve(PlatformCaller(), reference, ct));
    }

    private static CallerContext UserCaller() => new()
    {
        PrincipalId = Owner,
        AccountId = Owner,
        BrainId = Owner,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };

    private static CallerContext PlatformCaller() => new()
    {
        PrincipalId = "test-app",
        AccountId = Owner,
        BrainId = Owner,
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = "test-app",
    };
}
