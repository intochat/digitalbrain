namespace DigitalBrain.Runtime.Http;

/// <summary>
/// Configuration options for the runtime HTTP edge.
/// </summary>
public sealed class RuntimeEdgeOptions
{
    /// <summary>
    /// The secret key bytes used to sign and verify HMAC-SHA256 session bearer tokens.
    /// </summary>
    public byte[]? SecretKey { get; set; }

    /// <summary>
    /// Token lifetime before expiration (defaults to 1 hour).
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);
}
