using System.Text;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public sealed class BehaviorAuthor : IBehaviorAuthor
{
    private const string SystemInstructions =
        """
        You author DigitalBrain behavior programs.
        Return only complete C# source for a single-file behavior program.
        Include trigger records, IBehaviorProgram implementations, and IBehaviorInstallTests when needed.
        Do not wrap the program in markdown fences. Do not add commentary outside the source.
        Preserve working structure from the current program unless the approved scenarios require change.
        """;

    private readonly Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<string>> _complete;

    public BehaviorAuthor(Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<string>> complete)
    {
        ArgumentNullException.ThrowIfNull(complete);
        _complete = complete;
    }

    public static BehaviorAuthor ForChatClient(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        return new BehaviorAuthor(async (messages, cancellationToken) =>
        {
            var response = await chatClient
                .GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Text ?? string.Empty;
        });
    }

    public BehaviorScenarioProposal ProposeScenarios(BehaviorChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestText);

        var title = Collapse(request.RequestText);
        var baseFeature = string.IsNullOrWhiteSpace(request.CurrentFeatureText)
            ? $"Feature: {request.DisplayName}\n"
            : request.CurrentFeatureText.TrimEnd() + "\n";
        var proposed =
            baseFeature
            + $"  Scenario: {title}\n"
            + "    Given the requested change is approved\n"
            + "    When the behavior runs\n"
            + "    Then the outcome matches the request\n";

        return new BehaviorScenarioProposal(
            Guid.NewGuid().ToString("N"),
            proposed,
            DiffSummary: $"Add scenario '{title}' before any source generation.");
    }

    public async Task<BehaviorChangeResult> ApplyApprovedScenarios(
        BehaviorChangeRequest request,
        BehaviorScenarioProposal approved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentException.ThrowIfNullOrWhiteSpace(approved.ProposedFeatureText);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, SystemInstructions),
            new(ChatRole.User, BuildUserPrompt(request, approved)),
        };

        var raw = await _complete(messages, cancellationToken).ConfigureAwait(false);
        var program = StripCodeFence(raw);
        if (string.IsNullOrWhiteSpace(program))
        {
            throw new InvalidOperationException(
                "Behavior author model returned empty program source after scenario approval.");
        }

        return new BehaviorChangeResult(
            program,
            approved.ProposedFeatureText,
            request.FeatureName,
            ReadyForPropose: true);
    }

    private static string BuildUserPrompt(BehaviorChangeRequest request, BehaviorScenarioProposal approved)
    {
        var prompt = new StringBuilder();
        prompt.Append("Behavior id: ").Append(request.BehaviorId).AppendLine();
        prompt.Append("Display name: ").Append(request.DisplayName).AppendLine();
        prompt.Append("Feature name: ").Append(request.FeatureName).AppendLine();
        prompt.AppendLine("Owner request:");
        prompt.AppendLine(request.RequestText.Trim());
        prompt.AppendLine();
        prompt.AppendLine("Approved feature text:");
        prompt.AppendLine(approved.ProposedFeatureText.TrimEnd());
        prompt.AppendLine();
        prompt.AppendLine("Current program source:");
        prompt.AppendLine(
            string.IsNullOrWhiteSpace(request.CurrentProgramSource)
                ? "// empty"
                : request.CurrentProgramSource.TrimEnd());
        prompt.AppendLine();
        prompt.AppendLine("Emit the full updated C# program source only.");
        return prompt.ToString();
    }

    private static string StripCodeFence(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstLineBreak = text.IndexOf('\n', StringComparison.Ordinal);
        if (firstLineBreak < 0)
        {
            return string.Empty;
        }

        text = text[(firstLineBreak + 1)..];
        var closing = text.LastIndexOf("```", StringComparison.Ordinal);
        if (closing >= 0)
        {
            text = text[..closing];
        }

        return text.Trim();
    }

    private static string Collapse(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join(' ', parts);
        return joined.Length <= 80 ? joined : joined[..80].TrimEnd();
    }
}
