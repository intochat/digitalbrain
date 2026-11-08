using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain.Capabilities.Http;

/// <summary>
/// http@1: read-only HTTP GET against the hosts of one HTTP connection. An implementation refuses every other host with
/// <see cref="HostNotAllowedException"/>; it does so through <see cref="HttpHosts.Require"/>.
/// </summary>
[Capability("http", 1), NeuronAlias("http")]
public interface IHttpRead
{
    /// <summary>GETs <paramref name="url"/> and parses the body as JSON.</summary>
    /// <exception cref="HostNotAllowedException">The host is not in the connection's allow-list.</exception>
    Task<JsonElement> GetJson(string url, CancellationToken ct);

    /// <summary>GETs <paramref name="url"/> and returns the body as text.</summary>
    /// <exception cref="HostNotAllowedException">The host is not in the connection's allow-list.</exception>
    Task<string> GetText(string url, CancellationToken ct);
}
