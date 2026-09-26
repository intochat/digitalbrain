using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Sdk.Secrets;
using Xunit;

namespace DigitalBrain.Tests;

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
