namespace DigitalBrain.Google;

using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Orleans;

public sealed class GmailInoToolProvider(IGrainFactory grainFactory) : IInoToolProvider
{
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
                    var effectiveQuery = string.IsNullOrWhiteSpace(query) || query.Contains("last", StringComparison.OrdinalIgnoreCase)
                        ? ""
                        : query;
                    var ids = await gmail.ListMessagesAsync(effectiveQuery, Math.Clamp(maxResults, 1, 5), cancellationToken);
                    if (ids.Length == 0)
                    {
                        return "No matching Gmail messages found.";
                    }

                    var details = new List<string>();
                    foreach (var id in ids.Take(MaxEnrichedMessages))
                    {
                        var snippet = await gmail.ReadMessageAsync(id, cancellationToken);
                        details.Add($"ID:{id} - {snippet}");
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
            description: "Access Gmail for the user. Use for 'get my last gmail', 'recent emails', 'search gmail about X'. query can be Gmail syntax or natural (e.g. 'last', 'unread', 'Acme'). Returns enriched content.");

        var gatedTool = new AuthRequiredAIFunction(
            innerTool,
            ct => gmail.EnsureConnectedAsync(clientId, ct),
            "I've shown the user a Gmail sign-in prompt to connect their Google account. Tell them to check it and try again.");

        return [gatedTool];
    }
}
