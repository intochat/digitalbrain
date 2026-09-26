namespace DigitalBrain.Contracts.Types;

public enum SecretStatus
{
    Unset = 0,
    Set = 1,
}

[GenerateSerializer, Alias("semantic.secret-ref")]
public sealed record SecretRef
{
    [Id(0)] public string Reference { get; init; } = "";
    [Id(1)] public string Label { get; init; } = "";
    [Id(2)] public SecretStatus Status { get; init; }

    public bool IsSet => Status == SecretStatus.Set;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Owner
    {
        get
        {
            if (!IsReference(Reference))
            {
                throw new InvalidOperationException("Invalid secret reference.");
            }
            var slash = Reference.IndexOf('/', "secret://".Length);
            if (slash <= "secret://".Length)
            {
                throw new InvalidOperationException("The secret reference has no owner.");
            }
            return Reference["secret://".Length..slash];
        }
    }

    public static SecretRef For(string owner, string secretId, string label, bool isSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        return new SecretRef
        {
            Reference = $"secret://{owner}/{secretId}",
            Label = label,
            Status = isSet ? SecretStatus.Set : SecretStatus.Unset,
        };
    }

    public static bool IsReference(string? reference) =>
        !string.IsNullOrWhiteSpace(reference)
        && reference.StartsWith("secret://", StringComparison.Ordinal)
        && reference.Length > "secret://".Length;

    // Rebuilds the handle from the reference a vault returned, so a raw secret value can be posted
    // once to the vault and only the reference travels onward.
    public static SecretRef FromReference(string reference, string label = "")
    {
        if (!IsReference(reference))
        {
            throw new ArgumentException("A secret reference must look like 'secret://owner/id'.", nameof(reference));
        }

        return new SecretRef { Reference = reference, Label = label, Status = SecretStatus.Set };
    }
}
