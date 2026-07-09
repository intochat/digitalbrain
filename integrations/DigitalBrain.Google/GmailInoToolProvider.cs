namespace DigitalBrain.Google;

using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Orleans;

public sealed class GmailInoToolProvider(IGrainFactory grainFactory) : IInoToolProvider
{
    public string Provider => "google";
    private const string GmailGrainKey = "gmail-capability-main";
    private const int MaxEnrichedMessages = 3;

    public IReadOnlyList<AIFunction> BuildTools(string? clientId, CancellationToken cancellationToken)
    {
        var gmail = grainFactory.GetGrain<IGmailNeuron>(GmailGrainKey);

        var innerTool = AIFunctionFactory.Create(
            async (string query, int maxResults) =>
            {
                try
                {
                    // Deterministic for "last incoming" facts: prefer inbox recent (incoming) over arbitrary query.
                    // Empty or inbox query yields the most recent received messages from Gmail API.
                    var isLastIncoming = query.Contains("last", StringComparison.OrdinalIgnoreCase) ||
                                         query.Contains("incoming", StringComparison.OrdinalIgnoreCase) ||
                                         query.Contains("my gmail", StringComparison.OrdinalIgnoreCase);
                    var effectiveQuery = isLastIncoming ? "in:inbox" : (string.IsNullOrWhiteSpace(query) ? "" : query);
                    var ids = await gmail.ListMessagesForClientAsync(clientId, effectiveQuery, Math.Clamp(maxResults, 1, 5), cancellationToken);
                    if (ids.Length == 0)
                    {
                        return "No matching Gmail messages found.";
                    }

                    var details = new List<string>();
                    foreach (var id in ids.Take(MaxEnrichedMessages))
                    {
                        var snippet = await gmail.ReadMessageForClientAsync(clientId, id, cancellationToken);
                        details.Add($"MessageId:{id}; Snippet:{snippet}");
                    }

                    return "Gmail: " + string.Join(" | ", details);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "Gmail tool note: " + (ex.Message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("auth", StringComparison.OrdinalIgnoreCase)
                        ? "Connect Google account first."
                        : ex.Message);
                }
            },
            name: "gmail_get_messages",
            description: "Access Gmail for the user. Use for 'get my last gmail', 'last incoming gmail', 'recent emails', 'search gmail about X'. query can be Gmail syntax or natural (e.g. 'last', 'unread', 'Acme'). Returns enriched content with labeled snippets.");

        var gatedTool = new AuthRequiredAIFunction(
            innerTool,
            ct => gmail.EnsureConnectedAsync(clientId, ct),
            "I've shown the user a Gmail sign-in prompt to connect their Google account. Tell them to check it and try again.");

        return [gatedTool];
    }
}
