using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

internal sealed class GmailToolAdapter(
    IMcpIntegrationToolGateway integrations,
    ProviderInvocationPlanner planner,
    ToolResultComposer results,
    GmailAuthorizationResolver authorization)
{
    internal ToolOutcome SummarizeIncoming(RuntimeRequestContext context, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any())
            return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That Gmail request is not allowed.");
        if (!CanRead(context)) return ReadDenied();
        return new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: "I can’t summarize email content because Gmail access is limited to sender metadata. I won’t read bodies or snippets.");
    }

    internal async Task<ToolOutcome> ReadIncomingAtOffsetAsync(
        RuntimeRequestContext context,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        if (!TryParseIncomingRequest(input, out var request))
            return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That Gmail position cannot be read safely.");
        if (request.RequiresAnchor &&
            (string.IsNullOrWhiteSpace(request.AnchorMessageId) || request.AnchorInternalDate is null ||
             request.TraversalDepth > GmailTools.MaximumOffset))
            return new ToolOutcome(
                ToolOutcomeKind.PermanentFailure,
                SafeReason: "I can’t safely resolve that previous email from the immediately preceding turn. Ask for the latest incoming email to start again.");
        if (!CanRead(context)) return ReadDenied();

        var result = await integrations.ReadIncomingAtOffsetAsync(
            RequestScope.Id(context),
            request,
            cancellationToken);
        return result.Status == GmailReadStatus.Success
            ? results.GmailIncoming(result)
            : authorization.LegacyFailure(result);
    }

    internal async Task<ToolOutcome> InvokeTypedAsync(
        RuntimeRequestContext context,
        string toolId,
        SemanticIntentProposal proposal,
        CancellationToken cancellationToken)
    {
        if (!CanRead(context)) return ReadDenied();

        if (string.Equals(toolId, GmailTools.SummarizeThread, StringComparison.Ordinal))
        {
            if (!context.Grants.Contains("gmail.read.content"))
                return new ToolOutcome(
                    ToolOutcomeKind.Denied,
                    SafeReason: "Email content access is separate from Gmail metadata access. Grant gmail.read.content before asking for a summary.");
            return new ToolOutcome(
                ToolOutcomeKind.PermanentFailure,
                SafeReason: "Thread summaries are unavailable because this Gmail connection is metadata-only. No message body or snippet was read.");
        }

        var ownerScope = RequestScope.Id(context);
        if (string.Equals(toolId, GmailTools.ReadMailboxOverview, StringComparison.Ordinal))
        {
            var overview = await integrations.ReadGmailMailboxOverviewAsync(ownerScope, cancellationToken);
            return overview.Status == GmailReadStatus.Success
                ? results.GmailMailboxOverview(overview)
                : authorization.TypedFailure(overview.Status, overview.SafeReason, overview.ConnectionUrl);
        }

        var history = await planner.ReadHistoryAsync(context, cancellationToken).ConfigureAwait(false);
        if (!planner.TryCompileGmail(proposal, history, out var selection, out var offset, out var safeReason))
            return new ToolOutcome(ToolOutcomeKind.PermanentFailure, SafeReason: safeReason);

        if (string.Equals(toolId, GmailTools.ReadMessages, StringComparison.Ordinal))
        {
            var requestLimit = proposal.Ordinal is not null &&
                               proposal.Reference == SemanticReference.LatestProviderResult
                ? 1
                : proposal.Limit;
            var request = new GmailMessageListRequest(selection, offset, requestLimit);
            var messages = await integrations.ReadGmailMessagesAsync(ownerScope, request, cancellationToken);
            return messages.Status == GmailReadStatus.Success
                ? results.GmailMessages(request, messages)
                : authorization.TypedFailure(messages.Status, messages.SafeReason, messages.ConnectionUrl);
        }

        var threadRequest = new GmailThreadListRequest(selection, offset, proposal.Limit);
        var threads = await integrations.ReadGmailThreadsAsync(ownerScope, threadRequest, cancellationToken);
        return threads.Status == GmailReadStatus.Success
            ? results.GmailThreads(threadRequest, threads)
            : authorization.TypedFailure(threads.Status, threads.SafeReason, threads.ConnectionUrl);
    }

    internal async Task<GmailSenderResolution> ResolveLatestSenderAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken)
    {
        var history = await planner.ReadHistoryAsync(context, cancellationToken).ConfigureAwait(false);
        var senderAddress = planner.TryGetLatestGmailSender(history);
        if (senderAddress is not null) return new GmailSenderResolution(senderAddress);

        var gmail = await integrations.ReadGmailMessagesAsync(
            RequestScope.Id(context),
            new GmailMessageListRequest(new GmailMessageSelection(), Limit: 1),
            cancellationToken);
        return gmail.Status == GmailReadStatus.Success
            ? new GmailSenderResolution(gmail.Messages.FirstOrDefault()?.FromAddress)
            : new GmailSenderResolution(
                null,
                authorization.TypedFailure(gmail.Status, gmail.SafeReason, gmail.ConnectionUrl));
    }

    private static bool CanRead(RuntimeRequestContext context) =>
        context.Principal.Kind == PrincipalKind.User && context.Grants.Contains("gmail.read");

    private static ToolOutcome ReadDenied() => new(
        ToolOutcomeKind.Denied,
        SafeReason: "You don’t have permission to read Gmail in this workspace.");

    private static bool TryParseIncomingRequest(JsonElement input, out GmailReadRequest request)
    {
        request = new GmailReadRequest(-1);
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(static property => property.Name is not
                ("offset" or "anchorMessageId" or "anchorInternalDate" or "traversalDepth" or "requiresAnchor")))
            return false;
        try
        {
            request = input.Deserialize<GmailReadRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        catch (JsonException)
        {
            return false;
        }
        return request is not null &&
               request.Offset is >= 0 and <= GmailTools.MaximumOffset &&
               request.TraversalDepth is >= 0 and <= GmailTools.MaximumOffset + 1 &&
               (request.AnchorMessageId is null
                   ? request.AnchorInternalDate is null &&
                     (!request.RequiresAnchor || request.TraversalDepth == GmailTools.MaximumOffset + 1) &&
                     (request.RequiresAnchor || request.TraversalDepth == request.Offset)
                   : request.RequiresAnchor && request.Offset == 1 && request.AnchorInternalDate is >= 0 &&
                     request.AnchorMessageId.Length is > 0 and <= SemanticIntentValidator.MaximumTextLength);
    }
}

internal sealed record GmailSenderResolution(string? SenderAddress, ToolOutcome? Failure = null);

internal sealed class GmailAuthorizationResolver(
    ToolActionPolicy actionPolicy,
    SemanticIntentValidator validator)
{
    internal ToolOutcome LegacyFailure(GmailReadResult result) => result.Status switch
    {
        GmailReadStatus.NeedsAuth when actionPolicy.IsAllowedOpenUrl(
            OAuthCallbackPaths.GoogleProvider,
            "Connect Google",
            result.ConnectionUrl) =>
            new ToolOutcome(
                ToolOutcomeKind.NeedsAuth,
                SafeReason: result.SafeReason ?? "Connect your Google account to let INO read your Gmail.",
                Action: new ToolAction("openUrl", "Connect Google", result.ConnectionUrl!),
                AuthorizationProvider: OAuthCallbackPaths.GoogleProvider),
        GmailReadStatus.NeedsAuth => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: "Gmail connection is unavailable right now."),
        GmailReadStatus.ConfigurationMissing => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: result.SafeReason ?? "Gmail application configuration is missing."),
        _ => new ToolOutcome(
            ToolOutcomeKind.RetryableFailure,
            SafeReason: result.SafeReason ?? "I couldn’t read Gmail right now. Please try again later.")
    };

    internal ToolOutcome TypedFailure(GmailReadStatus status, string? safeReason, string? connectionUrl) => status switch
    {
        GmailReadStatus.NeedsAuth when actionPolicy.IsAllowedOpenUrl(
            OAuthCallbackPaths.GoogleProvider,
            "Connect Google",
            connectionUrl) => new ToolOutcome(
            ToolOutcomeKind.NeedsAuth,
            SafeReason: validator.SafeProviderReason(
                safeReason,
                "Connect your Google account to let INO read Gmail metadata."),
            Action: new ToolAction("openUrl", "Connect Google", connectionUrl!),
            AuthorizationProvider: OAuthCallbackPaths.GoogleProvider),
        GmailReadStatus.NeedsAuth => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: "Gmail connection is unavailable right now."),
        GmailReadStatus.ConfigurationMissing => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: validator.SafeProviderReason(safeReason, "Gmail application configuration is missing.")),
        GmailReadStatus.CapabilityUnavailable => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: validator.SafeProviderReason(
                safeReason,
                "That Gmail metadata capability is unavailable. No body or snippet was read.")),
        _ => new ToolOutcome(
            ToolOutcomeKind.RetryableFailure,
            SafeReason: "I couldn’t read Gmail metadata right now. Please try again later.")
    };
}
