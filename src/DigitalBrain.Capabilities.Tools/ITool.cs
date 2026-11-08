using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain.Capabilities.Tools;

/// <summary>tool@1: the tools of the connectors a brain has, called by name with JSON arguments.</summary>
[Capability("tool", 1), NeuronAlias("tool")]
public interface ITool
{
    /// <summary>Runs <paramref name="tool"/> with <paramref name="args"/> and returns its JSON result.</summary>
    Task<JsonElement> Run(string tool, JsonElement args, CancellationToken ct);

    /// <summary>The names of the tools that can be run.</summary>
    [ReadOnlyCall]
    Task<IReadOnlyList<string>> List();
}
