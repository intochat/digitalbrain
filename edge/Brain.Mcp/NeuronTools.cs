using System.ComponentModel;
using System.Text.Json;
using Brain.Contracts;
using ModelContextProtocol.Server;

internal sealed class NeuronTools(IClusterClient orleans)
{
    private static string DevCaller => "local-owner|actor/mcp-dev|session/dev";

    [McpServerTool(Name = "neuron_describe")]
    [Description("Describe a neuron: kind, revision, contracts.")]
    public async Task<string> Describe([Description("Neuron address key")] string address) =>
        JsonSerializer.Serialize(await orleans.GetGrain<INeuron>(address).DescribeAsync());

    [McpServerTool(Name = "neuron_read")]
    [Description("Read a bounded projection of a neuron.")]
    public async Task<string> Read(string address, string projection = "default") =>
        JsonSerializer.Serialize(await orleans.GetGrain<INeuron>(address).ReadAsync(projection));

    [McpServerTool(Name = "neuron_invoke")]
    [Description("Invoke a typed contract on a neuron. Replays are idempotent by commandId.")]
    public async Task<string> Invoke(string address, string contract, string inputJson, string commandId, long? expectedRevision = null) =>
        JsonSerializer.Serialize(await orleans.GetGrain<INeuron>(address)
            .InvokeAsync(new(contract, inputJson, commandId, DevCaller, expectedRevision)));
}
