using System.Text;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.MyData;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class VaultFacts
{
    private const string Owner = "owner-1";
    private const string Canary = "canary-secret-7f3a91";

    [Fact]
    public async Task SettingAFieldStoresTheCatalogCanonicalValue()
    {
        var state = NewState();
        var store = NewStore();

        await store.SetFieldAsync(state, "me.birthDate", FieldKind.Date, "1990-05-04");

        var export = store.Export(state);
        var field = Assert.Single(export.Fields);
        Assert.Equal("me.birthDate", field.FieldPath);
        Assert.Equal("1990-05-04", field.Value);
        Assert.False(field.IsSecret);
    }

    [Fact]
    public async Task AFieldValueIsMaskedInTheReadList()
    {
        var state = NewState();
        var store = NewStore();
        await store.SetFieldAsync(state, "me.birthDate", FieldKind.Date, "1990-05-04");

        var view = store.Read(state);

        var field = Assert.Single(view.Fields);
        Assert.True(field.IsSet);
        Assert.DoesNotContain("1990-05-04", field.Display, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADateRejectsFreeTextWithACatalogError()
    {
        var state = NewState();
        var store = NewStore();

        var error = await Assert.ThrowsAsync<TypeValidationException>(() =>
            store.SetFieldAsync(state, "me.birthDate", FieldKind.Date, "not-a-date"));

        Assert.Equal(FieldKind.Date, error.Kind);
        Assert.Contains("Date", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecretMustBeWrittenThroughSetSecret()
    {
        var state = NewState();
        var store = NewStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SetFieldAsync(state, "me.apiKey", FieldKind.Secret, Canary));
    }

    [Fact]
    public async Task TheSecretValueNeverAppearsInPersistedStateOrReadModels()
    {
        var state = NewState();
        var store = NewStore();

        var reference = await store.SetSecretAsync(state, "me.apiKey", "API key", Canary);

        Assert.True(reference.IsSet);
        Assert.StartsWith("secret://", reference.Reference, StringComparison.Ordinal);

        var persisted = SerializeState(state);
        Assert.DoesNotContain(Canary, persisted, StringComparison.Ordinal);

        var view = store.Read(state);
        var viewText = string.Join("\n", view.Fields.Select(f => $"{f.FieldPath} {f.Kind} {f.Display} {f.IsSecret}"));
        Assert.DoesNotContain(Canary, viewText, StringComparison.Ordinal);

        var export = store.Export(state);
        var exportText = string.Join("\n", export.Fields.Select(f => $"{f.FieldPath} {f.Value}"));
        Assert.DoesNotContain(Canary, exportText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecretResolvesOnlyThroughTheVaultAndOnlyForAnAppCaller()
    {
        var state = NewState();
        var store = NewStore();
        var reference = await store.SetSecretAsync(state, "me.apiKey", "API key", Canary);

        var resolved = await store.ResolveSecretAsync(state, reference);
        Assert.Equal(Canary, resolved);

        SecretResolutionPolicy.EnsureAllowed(AppCaller());
        var denied = Assert.Throws<SecretResolutionException>(() => SecretResolutionPolicy.EnsureAllowed(UserCaller()));
        Assert.DoesNotContain(Canary, denied.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CryptoShredRemovesAccessAndNoLongerResolves()
    {
        var state = NewState();
        var store = NewStore();
        var reference = await store.SetSecretAsync(state, "me.apiKey", "API key", Canary);
        await store.SetFieldAsync(state, "me.birthDate", FieldKind.Date, "1990-05-04");

        store.Erase(state);

        Assert.True(store.Read(state).Erased);
        Assert.Empty(store.Export(state).Fields);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResolveSecretAsync(state, reference));
    }

    [Fact]
    public async Task ErasedStateCanBeReusedWithAFreshDataKey()
    {
        var state = NewState();
        var store = NewStore();
        await store.SetSecretAsync(state, "me.apiKey", "API key", Canary);
        store.Erase(state);

        await store.SetFieldAsync(state, "me.email.work", FieldKind.Email, "work@example.com");

        var export = store.Export(state);
        Assert.Equal("work@example.com", Assert.Single(export.Fields).Value);
    }

    [Fact]
    public async Task EveryVaultActionLandsInTheAccessAuditTrail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MyDataModule>().StartAsync(ct);
        var vault = brain.Get<IVault>(Owner);
        var caller = UserCaller();

        await vault.SetField(caller, "me.birthDate", FieldKind.Date, "1990-05-04", ct);
        await vault.SetSecret(caller, "me.apiKey", "API key", Canary, ct);
        await vault.Read(caller, ct);
        await vault.Export(caller, ct);

        var audit = await vault.Audit(caller, 50, ct);
        Assert.Contains(audit, entry => entry.Action == "field.set" && entry.FieldPath == "me.birthDate");
        Assert.Contains(audit, entry => entry.Action == "secret.set" && entry.FieldPath == "me.apiKey");
        Assert.Contains(audit, entry => entry.Action == "read");
        Assert.Contains(audit, entry => entry.Action == "export");
        Assert.All(audit, entry => Assert.DoesNotContain(Canary, entry.FieldPath + entry.PrincipalId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task VaultSignalsCarryNoPlaintextValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MyDataModule>().StartAsync(ct);
        var vault = brain.Get<IVault>(Owner);
        await using var changes = await brain.Observe<VaultFieldChanged>(vault, ct);

        await vault.SetSecret(UserCaller(), "me.apiKey", "API key", Canary, ct);

        var signal = await changes.NextAsync(ct: ct);
        Assert.Equal("me.apiKey", signal.FieldPath);
        Assert.True(signal.IsSecret);
        Assert.DoesNotContain(Canary, $"{signal.Owner}{signal.FieldPath}", StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSecretResolverFetchesThroughTheVaultGrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MyDataModule>().StartAsync(ct);
        var vault = brain.Get<IVault>(Owner);
        var reference = await vault.SetSecret(UserCaller(), "me.apiKey", "API key", Canary, ct);

        ISecretResolver resolver = new SecretResolver(brain.Grains);

        var resolved = await resolver.ResolveAsync(reference, AppCaller(), ct);
        Assert.Equal(Canary, resolved);
    }

    [Fact]
    public async Task ResolvingASecretForAUserCallerIsDeniedByTheVault()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MyDataModule>().StartAsync(ct);
        var vault = brain.Get<IVault>(Owner);
        var reference = await vault.SetSecret(UserCaller(), "me.apiKey", "API key", Canary, ct);

        await Assert.ThrowsAnyAsync<Exception>(() => vault.ResolveSecret(UserCaller(), reference, ct));
    }

    [Fact]
    public void TheDpapiWrapperRoundTripsADataKey()
    {
        var key = VaultCipher.NewKey();
        var wrapper = new DpapiKeyWrapper();

        var wrapped = wrapper.Wrap(key);
        var unwrapped = wrapper.Unwrap(wrapped);

        Assert.Equal(key, unwrapped);
        Assert.DoesNotContain(Convert.ToBase64String(key), wrapped, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeyVaultAdapterRoundTripsADataKeyThroughTheFake()
    {
        var key = VaultCipher.NewKey();
        var vaultKeyWrapper = new KeyVaultKeyWrapper(new FakeKeyVault());

        var wrapped = vaultKeyWrapper.Wrap(key);

        Assert.Equal(key, vaultKeyWrapper.Unwrap(wrapped));
    }

    private static VaultState NewState() => new() { Owner = Owner };

    private static VaultStore NewStore() => new(new KeyVaultKeyWrapper(new FakeKeyVault()));

    private static string SerializeState(VaultState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine(state.WrappedOwnerKey);
        foreach (var field in state.Fields.Values)
        {
            builder.AppendLine(field.FieldPath).AppendLine(field.Label).AppendLine(field.SealedValue)
                .AppendLine(field.SealedCredentialKey).AppendLine(field.SealedSecret);
        }

        foreach (var entry in state.Audit)
        {
            builder.AppendLine(entry.Action).AppendLine(entry.FieldPath).AppendLine(entry.PrincipalId);
        }

        return builder.ToString();
    }

    private static CallerContext UserCaller() => new()
    {
        PrincipalId = Owner,
        AccountId = Owner,
        WorkspaceId = "workspace-1",
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };

    private static CallerContext AppCaller() => new()
    {
        PrincipalId = "app-principal",
        AccountId = Owner,
        WorkspaceId = "workspace-1",
        Kind = CallerKind.App,
        StampedBy = TrustedEdge.AppProxy,
        AppId = "app-1",
    };
}
