using DigitalBrain.CSharpExpert;
using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Tests;

internal sealed class ScriptedAgentScript
{
    public string Reply { get; set; } = "{}";

    public Exception? Throw { get; set; }

    public string? LastPrompt { get; set; }
}

[GrainType("csharp-expert.coding-agent")]
internal sealed class ScriptedCodingAgent(ScriptedAgentScript script) : Neuron, ICodingAgent
{
    public Task<string> Ask(string prompt, CancellationToken cancellationToken = default)
    {
        script.LastPrompt = prompt;
        return script.Throw is null
            ? Task.FromResult(script.Reply)
            : Task.FromException<string>(script.Throw);
    }
}

public sealed class ScriptedAgentModule : IModule
{
    public void Configure(ISiloBuilder silo) { }
}
