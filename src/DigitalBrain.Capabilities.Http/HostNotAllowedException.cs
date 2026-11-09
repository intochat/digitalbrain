using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Capabilities.Http;

/// <summary>The url names a host (or a scheme) the connection does not allow. The message carries the scheme and host, never the path or query.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Contract errors carry the host only.")]
public sealed class HostNotAllowedException : Exception
{
    /// <summary>Creates the refusal for a host.</summary>
    /// <param name="host">The refused host.</param>
    public HostNotAllowedException(string host)
        : this(host, null)
    {
    }

    /// <summary>Creates the refusal for a host reached over a scheme.</summary>
    /// <param name="host">The refused host.</param>
    /// <param name="scheme">The url's scheme, shown in the message; null for none.</param>
    public HostNotAllowedException(string host, string? scheme)
        : base($"http host-not-allowed: {(scheme is null ? host : scheme + "://" + host)}")
    {
        Host = host;
    }

    /// <summary>The refused host.</summary>
    public string Host { get; }
}
