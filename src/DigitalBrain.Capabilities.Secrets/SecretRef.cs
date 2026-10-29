using System.Text.Json.Serialization;

namespace DigitalBrain.Capabilities.Secrets;

/// <summary>Whether a <see cref="SecretRef"/> points at a stored value.</summary>
public enum SecretStatus
{
    /// <summary>No value is stored.</summary>
    Unset = 0,

    /// <summary>A value is stored.</summary>
    Set = 1,
}

/// <summary>
/// A pointer to a secret: its name, a display label and whether it is set. It never holds the value, so it is safe to
/// store in settings, log and show.
/// </summary>
public sealed record SecretRef
{
    /// <summary>The prefix of a deployment-provided secret reference (<see cref="ISecrets.Named"/>): <c>named:&lt;name&gt;</c>.</summary>
    public const string NamedPrefix = "named:";

    /// <summary>The secret's name in the brain.</summary>
    [JsonPropertyName("reference")]
    public string Reference { get; init; } = "";

    /// <summary>A human label, for example "GitHub token".</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    /// <summary>Whether a value is stored.</summary>
    [JsonPropertyName("status")]
    public SecretStatus Status { get; init; }

    /// <summary>True when <see cref="Status"/> is <see cref="SecretStatus.Set"/>.</summary>
    [JsonIgnore]
    public bool IsSet => Status == SecretStatus.Set;

    /// <summary>True when the reference names a deployment-provided secret (<see cref="NamedPrefix"/>).</summary>
    [JsonIgnore]
    public bool IsNamed => Reference?.StartsWith(NamedPrefix, StringComparison.Ordinal) == true;

    /// <summary>A reference to a secret that has no value yet.</summary>
    public static SecretRef Unset(string reference, string label) => new() { Reference = reference, Label = label, Status = SecretStatus.Unset };

    /// <summary><c>secret &lt;reference&gt; (set|unset)</c>; never a value.</summary>
    public override string ToString() => $"secret {Reference} ({(IsSet ? "set" : "unset")})";
}
