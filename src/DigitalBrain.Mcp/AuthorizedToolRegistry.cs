using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

internal sealed class AuthorizedToolRegistry(
    SemanticIntentValidator semanticIntent,
    GmailToolAdapter gmail,
    SalesforceToolAdapter salesforce,
    ToolResultComposer results)
{
    internal static AuthorizedToolRegistry Create(
        IMcpIntegrationToolGateway integrations,
        IInoConversationStore? conversations,
        ToolActionPolicy actionPolicy)
    {
        var semanticIntent = new SemanticIntentValidator();
        var planner = new ProviderInvocationPlanner(conversations, semanticIntent);
        var results = new ToolResultComposer(semanticIntent);
        var gmail = new GmailToolAdapter(
            integrations,
            planner,
            results,
            new GmailAuthorizationResolver(actionPolicy, semanticIntent));
        var salesforce = new SalesforceToolAdapter(
            integrations,
            planner,
            results,
            new SalesforceAuthorizationResolver(actionPolicy, semanticIntent, results),
            semanticIntent);
        return new AuthorizedToolRegistry(semanticIntent, gmail, salesforce, results);
    }

    internal async Task<ToolOutcome> InvokeAsync(
        RuntimeRequestContext context,
        ToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(invocation.ToolId, AssistantTools.Clarify, StringComparison.Ordinal))
            return Clarify(invocation.Input);

        if (semanticIntent.IsSemanticTool(invocation.ToolId))
        {
            if (!semanticIntent.TryParse(invocation.ToolId, invocation.Input, out var proposal))
                return new ToolOutcome(
                    ToolOutcomeKind.Denied,
                    SafeReason: "That connected-service request is not a valid typed operation.");
            if (invocation.ToolId.StartsWith("gmail.", StringComparison.Ordinal))
                return await gmail.InvokeTypedAsync(context, invocation.ToolId, proposal, cancellationToken);
            if (invocation.ToolId.StartsWith("salesforce.", StringComparison.Ordinal))
                return await salesforce.InvokeTypedAsync(context, invocation.ToolId, proposal, cancellationToken);
            return await InvokeCrossProviderAsync(context, proposal, cancellationToken);
        }

        if (string.Equals(invocation.ToolId, GmailTools.SummarizeIncoming, StringComparison.Ordinal))
            return gmail.SummarizeIncoming(context, invocation.Input);
        if (string.Equals(invocation.ToolId, GmailTools.ReadIncomingAtOffset, StringComparison.Ordinal))
            return await gmail.ReadIncomingAtOffsetAsync(context, invocation.Input, cancellationToken);

        return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That tool request is not allowed.");
    }

    private async Task<ToolOutcome> InvokeCrossProviderAsync(
        RuntimeRequestContext context,
        SemanticIntentProposal proposal,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User ||
            !context.Grants.Contains("gmail.read") ||
            !context.Grants.Contains("salesforce.read"))
            return new ToolOutcome(
                ToolOutcomeKind.Denied,
                SafeReason: "Matching Gmail to Salesforce requires both gmail.read and salesforce.read in this workspace.");

        var sender = await gmail.ResolveLatestSenderAsync(context, cancellationToken);
        if (sender.Failure is not null) return sender.Failure;
        if (!semanticIntent.IsValidProviderIdentifier(sender.SenderAddress) ||
            !sender.SenderAddress!.Contains('@', StringComparison.Ordinal))
            return results.InvalidTypedRequest("The latest Gmail result has no usable sender address to match.");

        return await salesforce.MatchAccountToSenderAsync(
            context,
            proposal,
            sender.SenderAddress,
            cancellationToken);
    }

    private static ToolOutcome Clarify(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object ||
            input.EnumerateObject().Count() != 1 ||
            !input.TryGetProperty("message", out var messageElement) ||
            messageElement.ValueKind != JsonValueKind.String ||
            messageElement.GetString() is not { } message ||
            message.Length is < 1 or > SemanticIntentValidator.MaximumTextLength ||
            message.Any(char.IsControl))
            return new ToolOutcome(
                ToolOutcomeKind.Denied,
                SafeReason: "That clarification is not safe to display.");
        return new ToolOutcome(ToolOutcomeKind.PermanentFailure, SafeReason: message);
    }
}
