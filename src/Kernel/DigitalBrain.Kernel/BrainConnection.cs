namespace DigitalBrain.Kernel;

internal sealed record BrainConnection(
    string ConnectionId,
    string Source,
    string SynapseAlias,
    string Target);

// The brain's connection table carries no stored id; the wire id shown to surfaces is the
// connection's own identity, stable across reads.
internal static class ConnectionIdentity
{
    public static string Of(string source, string synapseAlias, string target)
        => $"{source}|{synapseAlias}|{target}";
}
