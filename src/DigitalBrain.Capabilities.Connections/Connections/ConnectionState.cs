using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Capabilities.Connections;

internal readonly record struct SharedRecipient(string Value)
{
    public static SharedRecipient ForApp(string brainId, string app)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        return new SharedRecipient($"{brainId}/{app}");
    }

    public static SharedRecipient ForPrincipal(string principalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        return AppPrincipal.AppKeyOf(principalId) is { } appKey && appKey.IndexOf('/', StringComparison.Ordinal) is > 0 and var slash
            ? ForApp(appKey[..slash], appKey)
            : new SharedRecipient("principal:" + principalId);
    }
}

public class ConnectionState
{
    internal const string ValueSecret = "value";
    internal const string AccessSecret = "access";
    internal const string RefreshSecret = "refresh";

    public string AuthMethodId { get; set; } = "";
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;

    [JsonConverter(typeof(ConnectionSecretsJsonConverter))]
    public Dictionary<string, SealedSecret> Secrets { get; set; } = new(StringComparer.Ordinal);

    public string? Account { get; set; }
    public string Scopes { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastProbedAt { get; set; }
    public string? Detail { get; set; }
    public string[] SharedInto { get; set; } = [];

    internal bool IsConfigured => Status != ConnectionStatus.Disconnected && Secrets.Count > 0;

    internal void ApplySecretCredential(string authMethodId, SealedSecret sealedValue)
    {
        AuthMethodId = authMethodId;
        Secrets = new(StringComparer.Ordinal) { [ValueSecret] = sealedValue };
        ExpiresAt = null;
        Scopes = "";
    }

    internal void ApplyOAuthCredential(OAuthGrant grant, SealedSecret access, SealedSecret? refresh)
    {
        var secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
        {
            [AccessSecret] = access,
        };
        if (refresh is not null)
        {
            secrets[RefreshSecret] = refresh;
        }

        Secrets = secrets;
        ExpiresAt = grant.ExpiresAt;
        Scopes = grant.Scopes;
    }

    internal void ClearCredentials()
    {
        Secrets = new(StringComparer.Ordinal);
        ExpiresAt = null;
        Account = null;
    }

    internal void AdoptSharedMetadata(string authMethodId, string? account)
    {
        AuthMethodId = authMethodId;
        Account = account;
    }

    internal void ImportSealedCredentials(string authMethodId, string? account, Dictionary<string, SealedSecret> secrets)
    {
        AuthMethodId = authMethodId;
        Account = account;
        Secrets = new(secrets, StringComparer.Ordinal);
    }

    internal bool TryAddSharedRecipient(SharedRecipient recipient)
    {
        if (SharedInto.Contains(recipient.Value, StringComparer.Ordinal))
        {
            return false;
        }

        SharedInto = [.. SharedInto, recipient.Value];
        return true;
    }

    internal bool TryRemoveSharedRecipient(SharedRecipient recipient)
    {
        if (!SharedInto.Contains(recipient.Value, StringComparer.Ordinal))
        {
            return false;
        }

        SharedInto = [.. SharedInto.Where(shared => shared != recipient.Value)];
        return true;
    }

    internal bool IsSharedWith(SharedRecipient recipient)
        => SharedInto.Contains(recipient.Value, StringComparer.Ordinal);

    internal void CopyBaseFrom(ConnectionState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        AuthMethodId = source.AuthMethodId;
        Status = source.Status;
        Secrets = new(source.Secrets, StringComparer.Ordinal);
        Account = source.Account;
        Scopes = source.Scopes;
        ExpiresAt = source.ExpiresAt;
        LastProbedAt = source.LastProbedAt;
        Detail = source.Detail;
        SharedInto = [.. source.SharedInto];
    }
}

internal sealed class ConnectionSecretsJsonConverter : JsonConverter<Dictionary<string, SealedSecret>>
{
    public override Dictionary<string, SealedSecret> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, SealedSecret>(StringComparer.Ordinal);
        if (reader.TokenType == JsonTokenType.Null)
        {
            return result;
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var cipher = ReadProperty(prop.Value, "Cipher", "cipher", "Ciphertext", "ciphertext") ?? "";
                    var keyId = ReadProperty(prop.Value, "KeyId", "keyId") ?? "";
                    result[prop.Name] = new SealedSecret { Cipher = cipher, KeyId = keyId };
                }
            }
            return result;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { continue; }
                var name = ReadProperty(item, "Name", "name");
                if (string.IsNullOrEmpty(name)) { continue; }
                var cipher = ReadProperty(item, "Ciphertext", "ciphertext", "Cipher", "cipher") ?? "";
                var keyId = ReadProperty(item, "KeyId", "keyId") ?? "";
                result[name] = new SealedSecret { Cipher = cipher, KeyId = keyId };
            }
            return result;
        }

        throw new JsonException($"Unexpected JSON token {root.ValueKind} for ConnectionState.Secrets.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, SealedSecret> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value is not null)
        {
            foreach (var (name, secret) in value)
            {
                writer.WritePropertyName(name);
                JsonSerializer.Serialize(writer, secret, options);
            }
        }
        writer.WriteEndObject();
    }

    private static string? ReadProperty(JsonElement obj, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }
}
