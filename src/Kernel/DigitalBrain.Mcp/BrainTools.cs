using System.ComponentModel;
using DigitalBrain.Client;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class BrainTools(IDigitalBrain brain)
{
    [McpServerTool(Name = McpSurface.BrainConnect)]
    [Description(
        "Wire a connection in the owner's brain: facts the source emits under synapseAlias "
        + "are delivered to the target. Instances are written type:name; each "
        + "(source, synapseAlias) pair routes to exactly one target.")]
    public async Task<string> BrainConnectAsync(
        [Description("Source instance, for example 'timer:default'")] string source,
        [Description("The routed fact's contract id, for example 'time.timer-elapsed'")] string synapseAlias,
        [Description("Target instance, for example 'chat:main'")] string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(synapseAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        cancellationToken.ThrowIfCancellationRequested();

        var alias = synapseAlias.Trim();
        var connection = new Connection(
            ParseInstance(source, nameof(source)),
            alias,
            ParseInstance(target, nameof(target)));

        await brain.GetEntity<IBrain>(DigitalBrainNames.DefaultBrain).Connect(connection);
        return $"Connected {connection.From} --{alias}--> {connection.To}.";
    }

    private NeuronId ParseInstance(string instance, string parameterName)
    {
        var trimmed = instance.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            throw new ArgumentException(
                $"'{instance}' must be written type:name, for example 'chat:main'.",
                parameterName);
        }

        var type = trimmed[..separator];
        var rest = trimmed[(separator + 1)..];
        var name = rest.Contains('/', StringComparison.Ordinal)
            ? rest[(rest.IndexOf('/', StringComparison.Ordinal) + 1)..]
            : rest;

        return new NeuronId(type, brain.Owner, name);
    }
}
