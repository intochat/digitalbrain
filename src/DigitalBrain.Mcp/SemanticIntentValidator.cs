using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Mcp;

internal sealed class SemanticIntentValidator
{
    internal const int MaximumTextLength = 256;

    internal SemanticIntentValidator()
    {
        Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        Json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    internal JsonSerializerOptions Json { get; }

    internal bool IsSemanticTool(string toolId) => toolId is
        GmailTools.ReadMessages or
        GmailTools.ReadMailboxOverview or
        GmailTools.ReadThreads or
        GmailTools.SummarizeThread or
        SalesforceTools.DiscoverObjects or
        SalesforceTools.ReadRecords or
        SalesforceTools.SearchRecords or
        SalesforceTools.AggregateRecords or
        SalesforceTools.ContinueRecords or
        SalesforceTools.PreviewMutation or
        CrossProviderTools.MatchSalesforceAccountToGmailSender;

    internal bool TryParse(
        string toolId,
        JsonElement input,
        out SemanticIntentProposal proposal)
    {
        proposal = default!;
        if (input.ValueKind != JsonValueKind.Object || input.GetRawText().Length > 16 * 1024) return false;
        try
        {
            proposal = input.Deserialize<SemanticIntentProposal>(Json)!;
        }
        catch (JsonException)
        {
            return false;
        }

        if (proposal is null || proposal.Limit is < 1 or > GmailTools.MaximumResultCount ||
            proposal.Ordinal is < 1 or > GmailTools.MaximumResultCount ||
            proposal.Filters is { Count: > 8 } || proposal.Sorts is { Count: > 8 } ||
            !IsValidText(proposal.Entity, required: false) ||
            !IsValidText(proposal.SearchText, required: false) ||
            !IsValidText(proposal.Clarification, required: false) ||
            proposal.Filters?.Any(filter =>
                !IsValidText(filter.Field, required: true) || !IsValidText(filter.Value, required: false)) == true ||
            proposal.Sorts?.Any(sort => !IsValidText(sort.Field, required: true)) == true ||
            (proposal.Aggregate is { } aggregate &&
             (!IsValidText(aggregate.Field, required: false) ||
              !IsValidText(aggregate.GroupBy, required: false))))
            return false;

        return string.Equals(ExpectedTool(proposal), toolId, StringComparison.Ordinal);
    }

    internal bool IsValidText(string? value, bool required) =>
        value is null
            ? !required
            : value.Trim().Length is > 0 and <= MaximumTextLength && !value.Any(char.IsControl);

    internal bool IsValidProviderIdentifier(string? value) =>
        value is { Length: > 0 and <= MaximumTextLength } && !value.Any(char.IsControl);

    internal bool IsValidSalesforceRecordId(string? value) =>
        value is { Length: 15 or 18 } && value.All(char.IsLetterOrDigit);

    internal string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    internal bool IsAllAccessible(string? entity) =>
        string.IsNullOrWhiteSpace(entity) || NormalizeName(entity) is "all" or "allaccessible";

    internal string SafeProviderReason(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextLength || value.Any(char.IsControl) ||
            value.Contains("://", StringComparison.Ordinal)) return fallback;
        return value.Trim();
    }

    private static string? ExpectedTool(SemanticIntentProposal proposal) => proposal.Provider switch
    {
        SemanticProvider.Gmail => proposal.Operation switch
        {
            SemanticOperation.List or SemanticOperation.Refine or SemanticOperation.Previous => GmailTools.ReadMessages,
            SemanticOperation.Overview => GmailTools.ReadMailboxOverview,
            SemanticOperation.Threads => GmailTools.ReadThreads,
            SemanticOperation.Summarize => GmailTools.SummarizeThread,
            _ => null
        },
        SemanticProvider.Salesforce => proposal.Operation switch
        {
            SemanticOperation.Discover => SalesforceTools.DiscoverObjects,
            SemanticOperation.Search => SalesforceTools.SearchRecords,
            SemanticOperation.Aggregate => SalesforceTools.AggregateRecords,
            SemanticOperation.NextPage => SalesforceTools.ContinueRecords,
            SemanticOperation.MutationPreview => SalesforceTools.PreviewMutation,
            SemanticOperation.List or SemanticOperation.Refine or SemanticOperation.Related or
                SemanticOperation.Details or SemanticOperation.Previous => SalesforceTools.ReadRecords,
            _ => null
        },
        SemanticProvider.CrossProvider when proposal.Operation == SemanticOperation.Match &&
                                             proposal.Reference == SemanticReference.LatestGmailSender =>
            CrossProviderTools.MatchSalesforceAccountToGmailSender,
        _ => null
    };
}
