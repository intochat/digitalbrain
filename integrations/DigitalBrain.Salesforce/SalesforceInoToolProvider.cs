namespace DigitalBrain.Salesforce;

using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Orleans;

public sealed class SalesforceInoToolProvider(IGrainFactory grainFactory) : IInoToolProvider
{
    private const string SalesforceGrainKey = "salesforce-capability-main";

    public IReadOnlyList<AIFunction> BuildTools(string? clientId, CancellationToken cancellationToken)
    {
        var salesforce = grainFactory.GetGrain<ISalesforceCrmNeuron>(SalesforceGrainKey);

        var innerTool = AIFunctionFactory.Create(
            async (string soqlOrQuery, int maxResults) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(soqlOrQuery) && soqlOrQuery.Contains("select", StringComparison.OrdinalIgnoreCase))
                    {
                        var rows = await salesforce.QueryForClientAsync(clientId, soqlOrQuery, cancellationToken);
                        return "Salesforce query results: " + string.Join("; ", rows.Take(maxResults));
                    }

                    var accounts = await salesforce.ListAccountsForClientAsync(clientId, Math.Clamp(maxResults, 1, 20), cancellationToken);
                    return "Salesforce accounts: " + string.Join(", ", accounts.Take(maxResults));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return "Salesforce tool note: " + (ex.Message.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("login", StringComparison.OrdinalIgnoreCase)
                        ? "Connect Salesforce first."
                        : ex.Message);
                }
            },
            name: "salesforce_query",
            description: "Access Salesforce CRM. Use for 'list opportunities', 'get my deals', 'search accounts', 'recent leads'. soqlOrQuery: SOQL or natural description. Returns records.");

        var gatedTool = new AuthRequiredAIFunction(
            innerTool,
            ct => salesforce.EnsureConnectedAsync(clientId, ct),
            "I've shown the user a Salesforce sign-in prompt to connect their account. Tell them to check it and try again.");

        return [gatedTool];
    }
}
