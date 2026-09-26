using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Sdk.Secrets;

internal sealed class SecretsStore(IKeyWrapper keys)
{
    internal SecretRef Set(SecretsState state, string name, string label, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var ownerKey = OwnerKey(state);
        state.Fields[name] = new SecretRecord
        {
            FieldPath = name,
            Kind = FieldKind.Secret,
            IsSecret = true,
            Label = label,
            IsSet = true,
            SealedSecret = SecretCipher.Seal(ownerKey, value),
        };
        return SecretRef.For(state.Owner, name, label, true);
    }

    internal string Resolve(SecretsState state, SecretRef secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var prefix = $"secret://{state.Owner}/";
        if (!secret.Reference.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The secret belongs to another owner.");
        }

        var name = secret.Reference[prefix.Length..];
        if (string.IsNullOrEmpty(name) || !state.Fields.TryGetValue(name, out var record) || !record.IsSecret)
        {
            throw new InvalidOperationException("The secret is not present.");
        }

        var ownerKey = keys.Unwrap(state.WrappedOwnerKey);
        if (string.IsNullOrEmpty(record.SealedCredentialKey))
        {
            return SecretCipher.Open(ownerKey, record.SealedSecret);
        }

        // Secrets saved by MyData used a second key for each credential.
        var credentialKey = Convert.FromBase64String(SecretCipher.Open(ownerKey, record.SealedCredentialKey));
        return SecretCipher.Open(credentialKey, record.SealedSecret);
    }

    private byte[] OwnerKey(SecretsState state)
    {
        if (!string.IsNullOrEmpty(state.WrappedOwnerKey))
        {
            return keys.Unwrap(state.WrappedOwnerKey);
        }

        var key = SecretCipher.NewKey();
        state.WrappedOwnerKey = keys.Wrap(key);
        return key;
    }
}
