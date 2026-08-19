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
        ArgumentException.ThrowIfNullOrWhiteSpace(synapseAlias);
        cancellationToken.ThrowIfCancellationRequested();

        var alias = synapseAlias.Trim();
        var connection = new Connection(
            ParseInstance(source, nameof(source)),
            alias,
            ParseInstance(target, nameof(target)));

        await BrainGrain()
            .Connect(connection)
            .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken);
        return $"Connected {connection.From} --{alias}--> {connection.To}.";
    }

    [McpServerTool(Name = McpSurface.BrainDisconnect)]
    [Description(
        "Remove a connection from the owner's brain: the exact source, synapseAlias, and "
        + "target of an existing wire, as brain_connect wired it.")]
    public async Task<string> BrainDisconnectAsync(
        [Description("Source instance of the wire, for example 'timer:default'")] string source,
        [Description("The wire's synapse alias, for example 'time.timer-elapsed'")] string synapseAlias,
        [Description("Target instance of the wire, for example 'chat:main'")] string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(synapseAlias);
        cancellationToken.ThrowIfCancellationRequested();

        var alias = synapseAlias.Trim();
        var connection = new Connection(
            ParseInstance(source, nameof(source)),
            alias,
            ParseInstance(target, nameof(target)));

        var routed = await BrainGrain()
            .Connections(connection.From, alias)
            .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken);
        if (!routed.Any(existing => existing.To == connection.To))
        {
            return $"No wire {connection.From} --{alias}--> {connection.To} exists.";
        }

        await BrainGrain()
            .Disconnect(connection)
            .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken);
        return $"Disconnected {connection.From} --{alias}--> {connection.To}.";
    }

    private IBrain BrainGrain()
        => brain.GetEntity<IBrain>(DigitalBrainNames.DefaultBrain);

    private NeuronId ParseInstance(string instance, string parameterName)
        => NeuronId.TryParseInstance(instance, brain.Owner, out var id)
            ? id
            : throw new ArgumentException(
                $"'{instance}' must be written type:name, for example 'chat:main' — no owner segment.",
                parameterName);
}
