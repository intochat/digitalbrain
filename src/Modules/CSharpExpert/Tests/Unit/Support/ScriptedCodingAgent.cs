using DigitalBrain.CSharpExpert;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;
using Orleans.Hosting;

namespace DigitalBrain.Tests;

internal sealed class ScriptedAgentScript
{
    private readonly Lock gate = new();
    private readonly Queue<IReadOnlyList<EditRequest>> edits = new();
    private readonly List<EditProposal> proposals = [];

    public string Reply { get; set; } = "{}";

    public Exception? Throw { get; set; }

    public string? LastPrompt { get; set; }

    public IReadOnlyList<EditRequest> DefaultEdits { get; set; } = [];

    public void QueueEdits(IReadOnlyList<EditRequest> value)
    {
        lock (gate)
        {
            edits.Enqueue(value);
        }
    }

    public IReadOnlyList<EditRequest> NextEdits()
    {
        lock (gate)
        {
            return edits.Count > 0 ? edits.Dequeue() : DefaultEdits;
        }
    }

    public void RecordProposal(EditProposal proposal)
    {
        lock (gate)
        {
            proposals.Add(proposal);
        }
    }

    public IReadOnlyList<EditProposal> Proposals
    {
        get
        {
            lock (gate)
            {
                return proposals.ToArray();
            }
        }
    }
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

    public Task<IReadOnlyList<EditRequest>> ProposeEdits(EditProposal proposal, CancellationToken cancellationToken = default)
    {
        script.RecordProposal(proposal);
        return script.Throw is null
            ? Task.FromResult(script.NextEdits())
            : Task.FromException<IReadOnlyList<EditRequest>>(script.Throw);
    }
}

public sealed class ScriptedAgentModule : IModule
{
    public void Configure(ISiloBuilder silo) { }
}
