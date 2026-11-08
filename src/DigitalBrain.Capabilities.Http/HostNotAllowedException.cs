using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Capabilities.Http;

/// <summary>The url names a host (or a scheme) the connection does not allow.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Contract errors carry the host only.")]
public sealed class HostNotAllowedException(string host) : Exception($"http host-not-allowed: {host}")
{
    /// <summary>The refused host.</summary>
    public string Host { get; } = host;
}
