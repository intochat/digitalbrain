using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.Microsoft.Roslyn;
using Orleans;

namespace DigitalBrain.CSharpExpert;

internal sealed class AgentCodingBackend(IGrainFactory grains) : ICodingAgentBackend
{
    public async Task<string> AskAsync(string agentId, string prompt, CancellationToken cancellationToken)
    {
        var agent = grains.GetGrain<IAgent>($"csharp-expert.{agentId}");
        await agent.ClearHistory(cancellationToken).ConfigureAwait(false);
        return await agent.GetResponse(prompt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EditRequest>> ProposeEditsAsync(string agentId, EditProposal proposal, CancellationToken cancellationToken)
    {
        var reply = await AskAsync(agentId, EditPrompt(proposal), cancellationToken).ConfigureAwait(false);
        return EditReplyReader.Read(reply);
    }

    private static string EditPrompt(EditProposal proposal)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You implement one step of a C# change. Reply with a JSON array of edits and nothing else.");
        prompt.AppendLine($"Step {proposal.StepNumber}: {proposal.StepTitle}");
        prompt.AppendLine(proposal.StepDetail);
        prompt.AppendLine($"Files: {string.Join(", ", proposal.Files)}");
        if (proposal.Failure is { Length: > 0 } failure)
        {
            prompt.AppendLine("The previous attempt failed. Fix it:");
            prompt.AppendLine(failure);
        }

        prompt.AppendLine("Edit kinds and their fields:");
        prompt.AppendLine("- InsertMember: symbolId (\"T:Namespace.Type\"), source");
        prompt.AppendLine("- ReplaceMember: symbolId (\"M:Namespace.Type.Method\"), source");
        prompt.AppendLine("- AddUsing: path, namespace");
        prompt.AppendLine("- ReplaceRange: path, startLine, endLine, source");
        prompt.AppendLine("Example: [{\"kind\":\"InsertMember\",\"symbolId\":\"T:Sample.Inbox\",\"source\":\"public void Clear() => _items.Clear();\"}]");
        prompt.AppendLine("Rules: no /// <summary> comments, self-explanatory names, inline comments only when truly needed.");
        prompt.AppendLine("Project map:");
        prompt.AppendLine(proposal.ProjectMap);
        prompt.AppendLine("Current sources:");
        prompt.AppendLine(proposal.Sources);
        return prompt.ToString();
    }
}
