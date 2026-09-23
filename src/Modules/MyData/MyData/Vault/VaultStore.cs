using System.Globalization;
using System.Text.Json;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.MyData;

// Pure envelope-encryption logic over the persisted state. The neuron owns auditing and signals;
// this type owns the crypto so a test can inspect what is actually written to grain state.
internal sealed class VaultStore(IKeyWrapper keyWrapper)
{
    internal Task<VaultFieldRecord> SetFieldAsync(VaultState state, string fieldPath, FieldKind kind, string value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
            if (kind == FieldKind.Secret)
            {
                throw new InvalidOperationException("Secrets are written through SetSecret so the value never enters read results.");
            }

            var canonical = Canonicalize(kind, value);
            var record = new VaultFieldRecord
            {
                FieldPath = fieldPath,
                Kind = kind,
                IsSecret = false,
                IsSet = true,
                SealedValue = VaultCipher.Seal(OwnerKey(state), canonical),
            };
            state.Fields[fieldPath] = record;
            return Task.FromResult(record);
        }
        catch (Exception error)
        {
            return Task.FromException<VaultFieldRecord>(error);
        }
    }

    internal Task<SecretRef> SetSecretAsync(VaultState state, string fieldPath, string label, string value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            var credentialKey = VaultCipher.NewKey();
            var record = new VaultFieldRecord
            {
                FieldPath = fieldPath,
                Kind = FieldKind.Secret,
                IsSecret = true,
                Label = label,
                IsSet = true,
                SealedCredentialKey = VaultCipher.Seal(OwnerKey(state), Convert.ToBase64String(credentialKey)),
                SealedSecret = VaultCipher.Seal(credentialKey, value),
            };
            state.Fields[fieldPath] = record;
            return Task.FromResult(SecretRef.For(state.Owner, fieldPath, label, isSet: true));
        }
        catch (Exception error)
        {
            return Task.FromException<SecretRef>(error);
        }
    }

    internal Task<string> ResolveSecretAsync(VaultState state, SecretRef secret)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(secret);
            if (string.IsNullOrEmpty(state.WrappedOwnerKey))
            {
                throw new InvalidOperationException("The owner's data key was erased; the secret can no longer be resolved.");
            }

            var fieldPath = FieldPathOf(secret);
            if (!state.Fields.TryGetValue(fieldPath, out var record) || !record.IsSecret)
            {
                throw new InvalidOperationException("The secret is not present in this vault.");
            }

            var ownerKey = keyWrapper.Unwrap(state.WrappedOwnerKey);
            var credentialKey = Convert.FromBase64String(VaultCipher.Open(ownerKey, record.SealedCredentialKey));
            return Task.FromResult(VaultCipher.Open(credentialKey, record.SealedSecret));
        }
        catch (Exception error)
        {
            return Task.FromException<string>(error);
        }
    }

    internal VaultView Read(VaultState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var fields = state.Fields.Values
            .OrderBy(record => record.FieldPath, StringComparer.Ordinal)
            .Select(record => new VaultFieldView
            {
                FieldPath = record.FieldPath,
                Kind = record.Kind,
                Sensitivity = TypeCatalog.Get(record.Kind).Sensitivity.ToString(),
                IsSet = record.IsSet,
                Display = Mask(record),
                IsSecret = record.IsSecret,
            })
            .ToArray();
        return new VaultView
        {
            Owner = state.Owner,
            Fields = fields,
            Erased = string.IsNullOrEmpty(state.WrappedOwnerKey),
        };
    }

    internal VaultExport Export(VaultState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrEmpty(state.WrappedOwnerKey))
        {
            return new VaultExport { Owner = state.Owner, Fields = [] };
        }

        var ownerKey = keyWrapper.Unwrap(state.WrappedOwnerKey);
        var fields = state.Fields.Values
            .OrderBy(record => record.FieldPath, StringComparer.Ordinal)
            .Select(record => new VaultExportField
            {
                FieldPath = record.FieldPath,
                Kind = record.Kind,
                IsSecret = record.IsSecret,
                Value = record.IsSecret ? null : VaultCipher.Open(ownerKey, record.SealedValue),
            })
            .ToArray();
        return new VaultExport { Owner = state.Owner, Fields = fields };
    }

    internal void Erase(VaultState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.WrappedOwnerKey = "";
        state.Fields.Clear();
    }

    internal static string FieldPathOf(SecretRef secret)
    {
        var parts = secret.Reference.Split('/');
        return parts.Length >= 4 ? parts[^1] : secret.Reference;
    }

    internal static string OwnerOf(SecretRef secret)
    {
        var parts = secret.Reference.Split('/');
        return parts.Length >= 4 ? parts[2] : "";
    }

    private byte[] OwnerKey(VaultState state)
    {
        if (string.IsNullOrEmpty(state.WrappedOwnerKey))
        {
            var key = VaultCipher.NewKey();
            state.WrappedOwnerKey = keyWrapper.Wrap(key);
            return key;
        }

        return keyWrapper.Unwrap(state.WrappedOwnerKey);
    }

    private static string Mask(VaultFieldRecord record) =>
        record.IsSecret ? (record.IsSet ? "•••• set" : "•••• unset") : "••••";

    private static string Canonicalize(FieldKind kind, string value) =>
        TypeCatalog.Validate(value, kind) switch
        {
            string text => text,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            string[] array => JsonSerializer.Serialize(array),
            Reference reference => reference.EntityId,
            _ => throw new TypeValidationException(kind, value),
        };
}
