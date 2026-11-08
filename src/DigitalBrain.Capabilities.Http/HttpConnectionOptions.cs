namespace DigitalBrain.Capabilities.Http;

/// <summary>The settings of an HTTP connection.</summary>
/// <param name="AllowedHosts">The hosts the connection may reach: an exact host, or <c>*.example.org</c> for any subdomain. Any other host is refused with <see cref="HostNotAllowedException"/>.</param>
public sealed record HttpConnectionOptions(IReadOnlyList<string> AllowedHosts);
