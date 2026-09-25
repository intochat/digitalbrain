using DigitalBrain.CSharpExpert;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;

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

internal sealed class ScriptedCodingBackend(ScriptedAgentScript script) : ICodingAgentBackend
{
    public Task<string> AskAsync(string agentId, string prompt, CancellationToken cancellationToken)
    {
        script.LastPrompt = prompt;
        return script.Throw is null
            ? Task.FromResult(script.Reply)
            : Task.FromException<string>(script.Throw);
    }

    public Task<IReadOnlyList<EditRequest>> ProposeEditsAsync(string agentId, EditProposal proposal, CancellationToken cancellationToken)
    {
        script.RecordProposal(proposal);
        return script.Throw is null
            ? Task.FromResult(script.NextEdits())
            : Task.FromException<IReadOnlyList<EditRequest>>(script.Throw);
    }
}
