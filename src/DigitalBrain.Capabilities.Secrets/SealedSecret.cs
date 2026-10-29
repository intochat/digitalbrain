using System.Text.Json.Serialization;

namespace DigitalBrain.Capabilities.Secrets;

/// <summary>
/// A value sealed by secrets@1 (<see cref="ISecrets.Seal"/>), for example a TOTP seed. It holds ciphertext and the id of the
/// key that sealed it, never the plaintext, so it is safe to store in neuron state. Only <see cref="ISecrets.Open"/> with the
/// same context recovers the plaintext.
/// </summary>
public sealed record SealedSecret
{
    /// <summary>The ciphertext with its nonce and tag, base64.</summary>
    [JsonPropertyName("cipher")]
    public required string Cipher { get; init; }

    /// <summary>The id of the sealing key.</summary>
    [JsonPropertyName("keyId")]
    public required string KeyId { get; init; }
}
