using System.Security.Cryptography;
using System.Text;
using Brain.Core.Endpoints;

namespace Brain.Core.Graph;

// The physical partition is derived only from the outbound source address.
// It is an internal runtime key, not a client-visible topology identifier.
internal sealed class GraphShardResolver
{
    internal string Resolve(EndpointAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var material = string.Join('|', source.Workspace.Value, source.Module.Value, source.Role.Value, source.ScopeToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
