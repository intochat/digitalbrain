using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Sdk.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SecretsFacts
{
    private const string Owner = "owner-1";
    private const string Canary = "canary-secret-7f3a91";

    [Fact]
    public async Task StartupMigrationInterfaceTargetsTheExistingVaultGrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<SecretsModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IKeyWrapper>(new DataProtectionKeyWrapper(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()))).StartAsync(ct);
        var vault = brain.Get<ISecrets>(Owner);
        var secret = await vault.Set(UserCaller(), "api.key", "Key", Canary, ct);
        Assert.True(await brain.Get<ISecretKeyMigration>(Owner).EnsurePortable());
        await brain.DeactivateAsync(vault, ct);
        Assert.True(await brain.Get<ISecretKeyMigration>(Owner).EnsurePortable());
        Assert.Equal(Canary, await vault.Resolve(PlatformCaller(), secret, ct));
    }

    [Fact]
    public async Task ActivationRewrapsLegacyKeyAndFailedPersistenceRetainsOriginal()
    {
        var legacy = new KeyVaultKeyWrapper(new FakeKeyVault());
        var state = new SecretsState { Owner = Owner };
        var reference = new SecretsStore(legacy).Set(state, "api.key", "API key", Canary);
        var original = state.WrappedOwnerKey;
        var storage = new MigrationState(state) { FailWrite = true };
        var portable = new DataProtectionKeyWrapper(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        await Assert.ThrowsAsync<IOException>(() => new SecretsNeuron(storage, portable).OnActivateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(original, state.WrappedOwnerKey);
        storage.FailWrite = false;
        var migrated = new SecretsNeuron(storage, portable);
        await migrated.OnActivateAsync(TestContext.Current.CancellationToken);
        Assert.True(await migrated.EnsurePortable());
        Assert.StartsWith("dp2:", state.WrappedOwnerKey, StringComparison.Ordinal);
        Assert.Equal(Canary, new SecretsStore(portable).Resolve(state, reference));
        Assert.Equal(1, storage.Writes);
        await migrated.OnActivateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, storage.Writes);
    }

    private sealed class MigrationState(SecretsState state) : Orleans.Runtime.IPersistentState<SecretsState>
    {
        public SecretsState State { get; set; } = state;
        public string Etag => "test";
        public bool RecordExists => true;
        public bool FailWrite { get; set; }
        public int Writes { get; private set; }
        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
        public Task WriteStateAsync()
        {
            if (FailWrite) { throw new IOException("Injected migration failure."); }
            Writes++;
            return Task.CompletedTask;
        }
    }

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
    public void ResolvesSecretsWrittenByThePreviousVault()
    {
        var keys = new KeyVaultKeyWrapper(new FakeKeyVault());
        var ownerKey = SecretCipher.NewKey();
        var credentialKey = SecretCipher.NewKey();
        var state = new SecretsState
        {
            Owner = Owner,
            WrappedOwnerKey = keys.Wrap(ownerKey),
            Fields = new Dictionary<string, SecretRecord>
            {
                ["api.key"] = new()
                {
                    FieldPath = "api.key",
                    IsSecret = true,
                    SealedCredentialKey = SecretCipher.Seal(ownerKey, Convert.ToBase64String(credentialKey)),
                    SealedSecret = SecretCipher.Seal(credentialKey, Canary),
                },
            },
        };

        var reference = DigitalBrain.Contracts.Types.SecretRef.For(Owner, "api.key", "API key", true);
        Assert.Equal(Canary, new SecretsStore(keys).Resolve(state, reference));
    }

    [Fact]
    public async Task UserCannotResolveThroughGrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<SecretsModule>().StartAsync(ct);
        var secrets = brain.Get<ISecrets>(Owner);
        var reference = await secrets.Set(UserCaller(), "api.key", "API key", Canary, ct);

        await Assert.ThrowsAnyAsync<Exception>(() => secrets.Resolve(UserCaller(), reference, ct));
        Assert.Equal(Canary, await secrets.Resolve(PlatformCaller(), reference, ct));
    }

    private static CallerContext UserCaller() => new()
    {
        PrincipalId = Owner,
        AccountId = Owner,
        WorkspaceId = Owner,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };

    private static CallerContext PlatformCaller() => new()
    {
        PrincipalId = "test-app",
        AccountId = Owner,
        WorkspaceId = Owner,
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = "test-app",
    };
}
