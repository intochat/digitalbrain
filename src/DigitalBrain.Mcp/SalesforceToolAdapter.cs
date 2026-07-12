using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

internal sealed class SalesforceToolAdapter(
    IMcpIntegrationToolGateway integrations,
    ProviderInvocationPlanner planner,
    ToolResultComposer results,
    SalesforceAuthorizationResolver authorization,
    SemanticIntentValidator validator)
{
    internal async Task<ToolOutcome> InvokeTypedAsync(
        RuntimeRequestContext context,
        string toolId,
        SemanticIntentProposal proposal,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("salesforce.read"))
            return new ToolOutcome(
                ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Salesforce in this workspace.");

        if (string.Equals(toolId, SalesforceTools.PreviewMutation, StringComparison.Ordinal))
        {
            if (!context.Grants.Contains("salesforce.mutation.preview"))
                return new ToolOutcome(
                    ToolOutcomeKind.Denied,
                    SafeReason: "A Salesforce mutation request preview requires the separate salesforce.mutation.preview grant. No record was changed.");
            return PreviewMutation(proposal);
        }

        var ownerScope = RequestScope.Id(context);
        var history = await planner.ReadHistoryAsync(context, cancellationToken).ConfigureAwait(false);
        SalesforceReadResult result;
        string resultField;
        switch (toolId)
        {
            case SalesforceTools.DiscoverObjects:
                result = await integrations.DiscoverSalesforceObjectsAsync(
                    ownerScope,
                    new SalesforceDiscoveryRequest(Math.Min(50, Math.Max(1, proposal.Limit))),
                    cancellationToken);
                resultField = "salesforceObjects";
                break;
            case SalesforceTools.SearchRecords:
                if (string.IsNullOrWhiteSpace(proposal.SearchText))
                    return results.InvalidTypedRequest("Tell me what to search for in Salesforce.");
                var searchEntities = validator.IsAllAccessible(proposal.Entity)
                    ? null
                    : new[] { new SalesforceSemanticEntity(proposal.Entity!) };
                result = await integrations.SearchSalesforceRecordsAsync(
                    ownerScope,
                    new SalesforceSearchRequest(proposal.SearchText, searchEntities, proposal.Limit),
                    cancellationToken);
                resultField = "salesforceSearch";
                break;
            case SalesforceTools.AggregateRecords:
                if (!planner.TryCompileSalesforceAggregate(proposal, out var aggregate, out var aggregateReason))
                    return results.InvalidTypedRequest(aggregateReason);
                result = await integrations.AggregateSalesforceRecordsAsync(ownerScope, aggregate, cancellationToken);
                resultField = "salesforceAggregate";
                break;
            case SalesforceTools.ContinueRecords:
                if (!planner.TryGetSalesforceContinuation(history, out var continuation))
                    return results.InvalidTypedRequest(
                        "There is no stable Salesforce continuation to follow. Run the bounded read again.");
                result = await integrations.ContinueSalesforceRecordsAsync(
                    ownerScope,
                    new SalesforceContinuationRequest(continuation),
                    cancellationToken);
                resultField = "salesforceRecords";
                break;
            default:
                if (!planner.TryCompileSalesforceRead(proposal, history, out var read, out var readReason))
                    return results.InvalidTypedRequest(readReason);
                result = await integrations.ReadSalesforceRecordsAsync(ownerScope, read, cancellationToken);
                resultField = "salesforceRecords";
                break;
        }

        return Complete(result, resultField, proposal.Entity ?? planner.TryGetLatestSalesforceEntity(history));
    }

    internal async Task<ToolOutcome> MatchAccountToSenderAsync(
        RuntimeRequestContext context,
        SemanticIntentProposal proposal,
        string senderAddress,
        CancellationToken cancellationToken)
    {
        var salesforce = await integrations.SearchSalesforceRecordsAsync(
            RequestScope.Id(context),
            new SalesforceSearchRequest(
                senderAddress,
                [new SalesforceSemanticEntity(proposal.Entity ?? "account")],
                Math.Min(3, proposal.Limit)),
            cancellationToken);
        if (salesforce.Status == SalesforceReadStatus.Success && salesforce.ReturnedCount > 1)
            return results.InvalidTypedRequest(
                "More than one Salesforce account matched that sender. Please add an account name or domain.");
        return Complete(salesforce, "salesforceSearch", proposal.Entity ?? "account", senderAddress);
    }

    private ToolOutcome PreviewMutation(SemanticIntentProposal proposal)
    {
        var changes = proposal.Filters?.Where(static filter => filter.Operator == SemanticFilterOperator.Set).ToArray() ?? [];
        if (!validator.IsValidText(proposal.Entity, required: true) ||
            !validator.IsValidText(proposal.SearchText, required: true) ||
            changes.Length is < 1 or > 8)
            return results.InvalidTypedRequest(
                "A mutation preview needs one bounded record match and at least one typed field change.");
        return results.SalesforceMutationPreview(proposal, changes);
    }

    private ToolOutcome Complete(
        SalesforceReadResult result,
        string resultField,
        string? entity,
        string? matchedSender = null) =>
        result.Status == SalesforceReadStatus.Success
            ? results.Salesforce(result, resultField, entity, matchedSender)
            : authorization.Failure(result);
}

internal sealed class SalesforceAuthorizationResolver(
    ToolActionPolicy actionPolicy,
    SemanticIntentValidator validator,
    ToolResultComposer results)
{
    internal ToolOutcome Failure(SalesforceReadResult result) => result.Status switch
    {
        SalesforceReadStatus.NeedsAuth when actionPolicy.IsAllowedOpenUrl(
            OAuthCallbackPaths.SalesforceProvider,
            "Connect Salesforce",
            result.ConnectionUrl) => new ToolOutcome(
                ToolOutcomeKind.NeedsAuth,
                SafeReason: validator.SafeProviderReason(
                    result.SafeReason,
                    "Connect your Salesforce account to let INO read Salesforce."),
                Action: new ToolAction("openUrl", "Connect Salesforce", result.ConnectionUrl!),
                AuthorizationProvider: OAuthCallbackPaths.SalesforceProvider),
        SalesforceReadStatus.NeedsAuth => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: "Salesforce connection is unavailable right now."),
        SalesforceReadStatus.ConfigurationMissing => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: validator.SafeProviderReason(
                result.SafeReason,
                "Salesforce application configuration is missing.")),
        SalesforceReadStatus.AccessDenied => new ToolOutcome(
            ToolOutcomeKind.Denied,
            SafeReason: "Salesforce denied access to that object or field for the connected user."),
        SalesforceReadStatus.InvalidRequest => results.InvalidTypedRequest(
            validator.SafeProviderReason(
                result.SafeReason,
                "That Salesforce request is not supported by the accessible schema.")),
        SalesforceReadStatus.ContinuationExpired => results.InvalidTypedRequest(
            "That Salesforce continuation expired. Run the bounded read again."),
        SalesforceReadStatus.LimitReached => results.InvalidTypedRequest(
            validator.SafeProviderReason(
                result.SafeReason,
                "The Salesforce safety limit was reached. Narrow the request.")),
        _ => new ToolOutcome(
            ToolOutcomeKind.RetryableFailure,
            SafeReason: "I couldn’t read Salesforce right now. Please try again later.")
    };
}
