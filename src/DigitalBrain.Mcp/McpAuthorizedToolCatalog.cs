using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class McpAuthorizedToolCatalog : IAuthorizedToolCatalog
{
    private const int MaximumSemanticText = 256;
    private static readonly JsonSerializerOptions SemanticJson = CreateSemanticJson();
    private readonly IMcpIntegrationToolGateway _integrations;
    private readonly IInoConversationStore? _conversations;
    private readonly ToolActionPolicy _actionPolicy;

    public McpAuthorizedToolCatalog(
        IMcpIntegrationToolGateway integrations,
        IInoConversationStore? conversations = null,
        IConfiguration? configuration = null,
        ToolActionPolicy? actionPolicy = null)
    {
        _integrations = integrations;
        _conversations = conversations;
        _actionPolicy = actionPolicy ?? new ToolActionPolicy(
            configuration?["DigitalBrain:Salesforce:RedirectUri"]);
    }

    public async Task<ToolOutcome> InvokeAsync(
        RuntimeRequestContext context,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.tool.invoke", ActivityKind.Internal);
        activity?.SetTag("db.ino.tool_id", invocation.ToolId);
        var outcome = await InvokeCoreAsync(context, invocation, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.tool_outcome", outcome.Kind.ToString());
        activity?.SetTag("db.ino.has_grounding", outcome.Kind == ToolOutcomeKind.Success && outcome.Content is not null);
        if (outcome.Kind is ToolOutcomeKind.RetryableFailure or ToolOutcomeKind.PermanentFailure or
            ToolOutcomeKind.OutcomeUnknown)
            activity?.SetStatus(ActivityStatusCode.Error, outcome.Kind.ToString());
        return outcome;
    }

    private async Task<ToolOutcome> InvokeCoreAsync(
        RuntimeRequestContext context,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(invocation.ToolId, AssistantTools.Clarify, StringComparison.Ordinal))
        {
            if (invocation.Input.ValueKind != JsonValueKind.Object ||
                invocation.Input.EnumerateObject().Count() != 1 ||
                !invocation.Input.TryGetProperty("message", out var messageElement) ||
                messageElement.ValueKind != JsonValueKind.String ||
                messageElement.GetString() is not { } message ||
                message.Length is < 1 or > 256 || message.Any(char.IsControl))
                return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That clarification is not safe to display.");
            return new ToolOutcome(ToolOutcomeKind.PermanentFailure, SafeReason: message);
        }
        if (IsSemanticTool(invocation.ToolId))
        {
            if (!TryParseSemanticIntent(invocation.ToolId, invocation.Input, out var proposal))
                return new ToolOutcome(
                    ToolOutcomeKind.Denied,
                    SafeReason: "That connected-service request is not a valid typed operation.");
            if (invocation.ToolId.StartsWith("gmail.", StringComparison.Ordinal))
                return await InvokeTypedGmailAsync(context, invocation.ToolId, proposal, cancellationToken);
            if (invocation.ToolId.StartsWith("salesforce.", StringComparison.Ordinal))
                return await InvokeTypedSalesforceAsync(context, invocation.ToolId, proposal, cancellationToken);
            return await InvokeCrossProviderAsync(context, proposal, cancellationToken);
        }
        if (string.Equals(invocation.ToolId, GmailTools.SummarizeIncoming, StringComparison.Ordinal))
        {
            if (invocation.Input.ValueKind != JsonValueKind.Object || invocation.Input.EnumerateObject().Any())
                return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That Gmail request is not allowed.");
            if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("gmail.read"))
                return new ToolOutcome(
                    ToolOutcomeKind.Denied,
                    SafeReason: "You don’t have permission to read Gmail in this workspace.");
            return new ToolOutcome(
                ToolOutcomeKind.PermanentFailure,
                SafeReason: "I can’t summarize email content because Gmail access is limited to sender metadata. I won’t read bodies or snippets.");
        }
        if (string.Equals(invocation.ToolId, GmailTools.ReadIncomingAtOffset, StringComparison.Ordinal))
        {
            if (!TryParseGmailRequest(invocation.Input, out var gmailRequest))
                return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That Gmail position cannot be read safely.");
            if (gmailRequest.RequiresAnchor &&
                (string.IsNullOrWhiteSpace(gmailRequest.AnchorMessageId) || gmailRequest.AnchorInternalDate is null ||
                 gmailRequest.TraversalDepth > GmailTools.MaximumOffset))
                return new ToolOutcome(
                    ToolOutcomeKind.PermanentFailure,
                    SafeReason: "I can’t safely resolve that previous email from the immediately preceding turn. Ask for the latest incoming email to start again.");
            return await InvokeGmailAsync(context, gmailRequest, cancellationToken);
        }
        if (invocation.Input.ValueKind != JsonValueKind.Object || invocation.Input.EnumerateObject().Any())
            return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That tool request is not allowed.");
        return new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "That tool request is not allowed.");
    }

    private async Task<ToolOutcome> InvokeGmailAsync(
        RuntimeRequestContext context,
        GmailReadRequest request,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("gmail.read"))
            return new ToolOutcome(
                ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Gmail in this workspace.");

        var result = await _integrations.ReadIncomingAtOffsetAsync(RequestScope.Id(context), request, cancellationToken);
        return result.Status switch
        {
            GmailReadStatus.Success => new ToolOutcome(
                ToolOutcomeKind.Success,
                JsonSerializer.SerializeToElement(new
                {
                    incomingMessage = new
                    {
                        status = GmailMailboxStatus(result.MailboxState),
                        sender = result.Sender,
                        senderAddress = result.SenderAddress,
                        messageId = result.MessageId,
                        internalDate = result.InternalDate,
                        traversalDepth = result.TraversalDepth,
                        anchoredPrevious = result.AnchoredPrevious
                    }
                }),
                GroundingContent: JsonSerializer.SerializeToElement(new
                {
                    incomingMessage = new
                    {
                        senderAddress = result.SenderAddress,
                        messageId = result.MessageId,
                        internalDate = result.InternalDate,
                        traversalDepth = result.TraversalDepth,
                        anchoredPrevious = result.AnchoredPrevious
                    }
                })),
            GmailReadStatus.NeedsAuth when _actionPolicy.IsAllowedOpenUrl("Connect Google", result.ConnectionUrl) => new ToolOutcome(
                ToolOutcomeKind.NeedsAuth,
                SafeReason: result.SafeReason ?? "Connect your Google account to let INO read your Gmail.",
                Action: new ToolAction("openUrl", "Connect Google", result.ConnectionUrl!)),
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
    }

    private async Task<ToolOutcome> InvokeTypedGmailAsync(
        RuntimeRequestContext context,
        string toolId,
        SemanticIntentProposal proposal,
        CancellationToken cancellationToken)
    {
        if (context.Principal.Kind != PrincipalKind.User || !context.Grants.Contains("gmail.read"))
            return new ToolOutcome(
                ToolOutcomeKind.Denied,
                SafeReason: "You don’t have permission to read Gmail in this workspace.");

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
            var overview = await _integrations.ReadGmailMailboxOverviewAsync(ownerScope, cancellationToken);
            if (overview.Status != GmailReadStatus.Success)
                return GmailFailure(overview.Status, overview.SafeReason, overview.ConnectionUrl);
            return new ToolOutcome(
                ToolOutcomeKind.Success,
                JsonSerializer.SerializeToElement(new
                {
                    gmailMailboxOverview = new
                    {
                        overview.InboxMessages,
                        overview.UnreadInboxMessages,
                        overview.InboxThreads,
                        overview.UnreadInboxThreads
                    }
                }, SemanticJson));
        }

        if (!TryCompileGmailRequest(context, proposal, out var selection, out var offset, out var safeReason))
            return new ToolOutcome(ToolOutcomeKind.PermanentFailure, SafeReason: safeReason);

        if (string.Equals(toolId, GmailTools.ReadMessages, StringComparison.Ordinal))
        {
            var requestLimit = proposal.Ordinal is not null &&
                               proposal.Reference == SemanticReference.LatestProviderResult
                ? 1
                : proposal.Limit;
            var request = new GmailMessageListRequest(selection, offset, requestLimit);
            var result = await _integrations.ReadGmailMessagesAsync(ownerScope, request, cancellationToken);
            if (result.Status != GmailReadStatus.Success)
                return GmailFailure(result.Status, result.SafeReason, result.ConnectionUrl);
            var stableIds = (selection.PinnedMessageIds ?? result.StableCandidateMessageIds ??
                             result.Messages.Select(static message => message.MessageId).ToArray())
                .Where(ValidProviderIdentifier)
                .Distinct(StringComparer.Ordinal)
                .Take(GmailTools.MaximumCandidateCount)
                .ToArray();
            var consumedCandidates = selection.PinnedMessageIds is null
                ? result.Messages.Length
                : Math.Min(request.Limit, Math.Max(0, stableIds.Length - offset));
            var nextOffset = checked(offset + consumedCandidates);
            var hasMore = nextOffset < stableIds.Length;
            var stableSelection = selection with { PinnedMessageIds = stableIds.Length == 0 ? null : stableIds };
            var display = JsonSerializer.SerializeToElement(new
            {
                gmailMessages = new
                {
                    messages = result.Messages,
                    coverage = result.Coverage,
                    hasMore
                }
            }, SemanticJson);
            var grounding = JsonSerializer.SerializeToElement(new
            {
                gmailMessages = new
                {
                    resultMessageIds = result.Messages.Select(static message => message.MessageId).ToArray(),
                    senderAddresses = result.Messages.Select(static message => message.FromAddress)
                        .Where(static value => value is not null).ToArray(),
                    selection = stableSelection,
                    nextOffset,
                    hasMore
                }
            }, SemanticJson);
            return new ToolOutcome(
                ToolOutcomeKind.Success,
                display,
                GroundingContent: grounding);
        }

        var threadRequest = new GmailThreadListRequest(selection, offset, proposal.Limit);
        var threads = await _integrations.ReadGmailThreadsAsync(ownerScope, threadRequest, cancellationToken);
        if (threads.Status != GmailReadStatus.Success)
            return GmailFailure(threads.Status, threads.SafeReason, threads.ConnectionUrl);
        var stableThreadCandidateIds = (threads.StableCandidateMessageIds ?? threads.Threads
                .SelectMany(static thread => thread.Messages)
                .Select(static message => message.MessageId).ToArray())
            .Where(ValidProviderIdentifier)
            .Distinct(StringComparer.Ordinal)
            .Take(GmailTools.MaximumCandidateCount)
            .ToArray();
        var nextThreadOffset = checked(offset + threads.Threads.Length);
        var stableThreadIds = (threads.StableCandidateThreadIds ?? threads.Threads
                .Select(static thread => thread.ThreadId).ToArray())
            .Where(ValidProviderIdentifier)
            .Distinct(StringComparer.Ordinal)
            .Take(GmailTools.MaximumCandidateCount)
            .ToArray();
        var hasMoreThreads = nextThreadOffset < stableThreadIds.Length;
        var stableThreadSelection = selection with
        {
            PinnedMessageIds = stableThreadCandidateIds.Length == 0 ? null : stableThreadCandidateIds
        };
        var threadDisplay = JsonSerializer.SerializeToElement(new
        {
            gmailThreads = new
            {
                threads = threads.Threads,
                coverage = threads.Coverage,
                hasMore = hasMoreThreads
            }
        }, SemanticJson);
        var threadGrounding = JsonSerializer.SerializeToElement(new
        {
            gmailThreads = new
            {
                resultMessageIds = threads.Threads.SelectMany(static thread => thread.Messages)
                    .Select(static message => message.MessageId).ToArray(),
                threadIds = threads.Threads.Select(static thread => thread.ThreadId).ToArray(),
                stableThreadIds,
                senderAddresses = threads.Threads.SelectMany(static thread => thread.Messages)
                    .Select(static message => message.FromAddress)
                    .Where(static value => value is not null).ToArray(),
                selection = stableThreadSelection,
                nextOffset = nextThreadOffset,
                hasMore = hasMoreThreads
            }
        }, SemanticJson);
        return new ToolOutcome(
            ToolOutcomeKind.Success,
            threadDisplay,
            GroundingContent: threadGrounding);
    }

    private async Task<ToolOutcome> InvokeTypedSalesforceAsync(
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
            return PreviewSalesforceMutation(proposal);
        }

        var ownerScope = RequestScope.Id(context);
        SalesforceReadResult result;
        string resultField;
        switch (toolId)
        {
            case SalesforceTools.DiscoverObjects:
                result = await _integrations.DiscoverSalesforceObjectsAsync(
                    ownerScope,
                    new SalesforceDiscoveryRequest(Math.Min(50, Math.Max(1, proposal.Limit))),
                    cancellationToken);
                resultField = "salesforceObjects";
                break;
            case SalesforceTools.SearchRecords:
                if (string.IsNullOrWhiteSpace(proposal.SearchText))
                    return InvalidTypedRequest("Tell me what to search for in Salesforce.");
                var searchEntities = IsAllAccessible(proposal.Entity)
                    ? null
                    : new[] { new SalesforceSemanticEntity(proposal.Entity!) };
                result = await _integrations.SearchSalesforceRecordsAsync(
                    ownerScope,
                    new SalesforceSearchRequest(proposal.SearchText, searchEntities, proposal.Limit),
                    cancellationToken);
                resultField = "salesforceSearch";
                break;
            case SalesforceTools.AggregateRecords:
                if (!TryCompileSalesforceAggregate(proposal, out var aggregate, out var aggregateReason))
                    return InvalidTypedRequest(aggregateReason);
                result = await _integrations.AggregateSalesforceRecordsAsync(ownerScope, aggregate, cancellationToken);
                resultField = "salesforceAggregate";
                break;
            case SalesforceTools.ContinueRecords:
                if (!TryGetSalesforceContinuation(context, out var continuation))
                    return InvalidTypedRequest("There is no stable Salesforce continuation to follow. Run the bounded read again.");
                result = await _integrations.ContinueSalesforceRecordsAsync(
                    ownerScope,
                    new SalesforceContinuationRequest(continuation),
                    cancellationToken);
                resultField = "salesforceRecords";
                break;
            default:
                if (!TryCompileSalesforceRead(context, proposal, out var read, out var readReason))
                    return InvalidTypedRequest(readReason);
                result = await _integrations.ReadSalesforceRecordsAsync(ownerScope, read, cancellationToken);
                resultField = "salesforceRecords";
                break;
        }

        return SalesforceOutcome(result, resultField, proposal.Entity ?? TryGetLatestSalesforceEntity(context));
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

        var senderAddress = TryGetLatestGmailSender(context);
        if (senderAddress is null)
        {
            var gmail = await _integrations.ReadGmailMessagesAsync(
                RequestScope.Id(context),
                new GmailMessageListRequest(new GmailMessageSelection(), Limit: 1),
                cancellationToken);
            if (gmail.Status != GmailReadStatus.Success)
                return GmailFailure(gmail.Status, gmail.SafeReason, gmail.ConnectionUrl);
            senderAddress = gmail.Messages.FirstOrDefault()?.FromAddress;
        }
        if (!ValidProviderIdentifier(senderAddress) || !senderAddress!.Contains('@', StringComparison.Ordinal))
            return InvalidTypedRequest("The latest Gmail result has no usable sender address to match.");

        var salesforce = await _integrations.SearchSalesforceRecordsAsync(
            RequestScope.Id(context),
            new SalesforceSearchRequest(
                senderAddress,
                [new SalesforceSemanticEntity(proposal.Entity ?? "account")],
                Math.Min(3, proposal.Limit)),
            cancellationToken);
        if (salesforce.Status == SalesforceReadStatus.Success && salesforce.ReturnedCount > 1)
            return InvalidTypedRequest("More than one Salesforce account matched that sender. Please add an account name or domain.");
        return SalesforceOutcome(salesforce, "salesforceSearch", proposal.Entity ?? "account", senderAddress);
    }

    private static ToolOutcome PreviewSalesforceMutation(SemanticIntentProposal proposal)
    {
        var changes = proposal.Filters?.Where(static filter => filter.Operator == SemanticFilterOperator.Set).ToArray() ?? [];
        if (!ValidSemanticText(proposal.Entity, required: true) ||
            !ValidSemanticText(proposal.SearchText, required: true) ||
            changes.Length is < 1 or > 8)
            return InvalidTypedRequest("A mutation preview needs one bounded record match and at least one typed field change.");
        return new ToolOutcome(
            ToolOutcomeKind.Success,
            JsonSerializer.SerializeToElement(new
            {
                salesforceMutationPreview = new
                {
                    entity = proposal.Entity,
                    recordMatch = proposal.SearchText,
                    changes = changes.Select(static change => new { field = change.Field, value = change.Value }).ToArray(),
                    status = "previewOnly",
                    note = "This request has not been schema-verified and no Salesforce record was changed. A separately authorized, journaled confirmation operation is required."
                }
            }, SemanticJson));
    }

    private bool TryCompileGmailRequest(
        RuntimeRequestContext context,
        SemanticIntentProposal proposal,
        out GmailMessageSelection selection,
        out int offset,
        out string safeReason)
    {
        selection = GmailSelectionForEntity(proposal.Entity);
        offset = Math.Max(0, (proposal.Ordinal ?? 1) - 1);
        safeReason = "That Gmail selection could not be compiled safely.";

        if (proposal.Reference != SemanticReference.None)
        {
            var grounding = LatestGrounding(context, "gmail.");
            if (grounding is null)
            {
                safeReason = "There is no grounded Gmail result to refine. Run a Gmail read first.";
                return false;
            }

            if (proposal.Operation == SemanticOperation.Previous)
            {
                if (!TryGetGmailSelection(grounding.Content, out selection) ||
                    !TryGetInt32(grounding.Content, "nextOffset", out var nextOffset) ||
                    !TryGetBoolean(grounding.Content, "hasMore", out var hasMore) || !hasMore)
                {
                    safeReason = "The prior bounded Gmail result has no stable next item.";
                    return false;
                }
                offset = checked(nextOffset + Math.Max(0, (proposal.Ordinal ?? 1) - 1));
            }
            else if (proposal.Reference == SemanticReference.LatestProviderResult)
            {
                if (TryGetGmailSelection(grounding.Content, out var priorSelection))
                    selection = priorSelection;
                var pinnedIds = GmailMessageIds(grounding.Content).Take(GmailTools.MaximumResultCount).ToArray();
                if (pinnedIds.Length == 0)
                {
                    safeReason = "The prior Gmail result has no stable message identifiers to refine.";
                    return false;
                }
                selection = selection with { PinnedMessageIds = pinnedIds };
            }
            else if (proposal.Reference is SemanticReference.SameSender or SemanticReference.LatestGmailSender)
            {
                var sender = GmailSender(grounding.Content);
                if (!ValidProviderIdentifier(sender) || !sender!.Contains('@', StringComparison.Ordinal))
                {
                    safeReason = "The prior Gmail result has no stable sender address to reuse.";
                    return false;
                }
                selection = selection with { SenderAddress = sender, PinnedMessageIds = null };
                offset = 0;
            }
        }

        foreach (var filter in proposal.Filters ?? [])
        {
            var field = NormalizeSemanticName(filter.Field);
            var value = filter.Value?.Trim();
            switch (field)
            {
                case "readstate":
                case "read":
                    selection = value?.Equals("unread", StringComparison.OrdinalIgnoreCase) == true
                        ? selection with { ReadState = GmailMessageReadState.Unread }
                        : value?.Equals("read", StringComparison.OrdinalIgnoreCase) == true
                            ? selection with { ReadState = GmailMessageReadState.Read }
                            : selection;
                    if (selection.ReadState == GmailMessageReadState.Any) return false;
                    break;
                case "attachment":
                case "attachments":
                    selection = value is not null &&
                                (value.Equals("present", StringComparison.OrdinalIgnoreCase) ||
                                 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                 value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                        ? selection with { AttachmentFilter = GmailAttachmentFilter.HasAttachments }
                        : value is not null &&
                          (value.Equals("absent", StringComparison.OrdinalIgnoreCase) ||
                           value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                           value.Equals("no", StringComparison.OrdinalIgnoreCase))
                            ? selection with { AttachmentFilter = GmailAttachmentFilter.NoAttachments }
                            : selection;
                    if (selection.AttachmentFilter == GmailAttachmentFilter.Any) return false;
                    break;
                case "sender":
                case "from":
                    if (!ValidProviderIdentifier(value)) return false;
                    selection = selection with { SenderAddress = value };
                    break;
                case "recipient":
                case "to":
                    if (!ValidProviderIdentifier(value)) return false;
                    selection = selection with { RecipientAddress = value };
                    break;
                case "subject":
                    if (!ValidProviderIdentifier(value)) return false;
                    selection = selection with { SubjectContains = value };
                    break;
                default:
                    return false;
            }
        }

        if (proposal.TimeRange != SemanticTimeRange.None)
        {
            if (!TryGetTimeRange(proposal.TimeRange, out var from, out var until)) return false;
            selection = selection with
            {
                ReceivedAfterInclusive = from.ToUnixTimeMilliseconds(),
                ReceivedBeforeExclusive = until.ToUnixTimeMilliseconds()
            };
        }

        return offset < GmailTools.MaximumCandidateCount &&
               (selection.PinnedMessageIds is null || offset < selection.PinnedMessageIds.Length) &&
               selection.MaxPages is >= 1 and <= GmailTools.MaximumPageCount &&
               selection.MaxCandidates is >= 1 and <= GmailTools.MaximumCandidateCount;
    }

    private static GmailMessageSelection GmailSelectionForEntity(string? entity) =>
        NormalizeSemanticName(entity ?? string.Empty) switch
        {
            "inbox" => new GmailMessageSelection(GmailMailboxScope.Inbox),
            "sent" or "sentmail" => new GmailMessageSelection(GmailMailboxScope.Sent),
            "draft" or "drafts" => new GmailMessageSelection(GmailMailboxScope.Drafts),
            "all" or "allmail" => new GmailMessageSelection(GmailMailboxScope.All),
            _ => new GmailMessageSelection(GmailMailboxScope.Incoming)
        };

    private ToolOutcome GmailFailure(GmailReadStatus status, string? safeReason, string? connectionUrl) => status switch
    {
        GmailReadStatus.NeedsAuth when _actionPolicy.IsAllowedOpenUrl("Connect Google", connectionUrl) => new ToolOutcome(
            ToolOutcomeKind.NeedsAuth,
            SafeReason: SafeProviderReason(safeReason, "Connect your Google account to let INO read Gmail metadata."),
            Action: new ToolAction("openUrl", "Connect Google", connectionUrl!)),
        GmailReadStatus.NeedsAuth => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: "Gmail connection is unavailable right now."),
        GmailReadStatus.ConfigurationMissing => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: SafeProviderReason(safeReason, "Gmail application configuration is missing.")),
        GmailReadStatus.CapabilityUnavailable => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: SafeProviderReason(safeReason, "That Gmail metadata capability is unavailable. No body or snippet was read.")),
        _ => new ToolOutcome(
            ToolOutcomeKind.RetryableFailure,
            SafeReason: "I couldn’t read Gmail metadata right now. Please try again later.")
    };

    private bool TryCompileSalesforceRead(
        RuntimeRequestContext context,
        SemanticIntentProposal proposal,
        out SalesforceRecordReadRequest request,
        out string safeReason)
    {
        request = default!;
        safeReason = "That Salesforce read could not be compiled safely.";
        if (!ValidSemanticText(proposal.Entity, required: true))
        {
            safeReason = "Name the Salesforce record type you want to read.";
            return false;
        }
        if (!TryCompileSalesforceFilters(proposal, out var filters)) return false;
        var kind = proposal.Operation switch
        {
            SemanticOperation.Details => SalesforceRecordReadKind.Details,
            SemanticOperation.Related => SalesforceRecordReadKind.Related,
            _ => SalesforceRecordReadKind.List
        };
        SalesforceResolvedRecord? record = null;
        SalesforceResolvedRecord? relatedTo = null;
        if (proposal.Reference is (SemanticReference.LatestProviderResult or SemanticReference.SameAccount) &&
            (kind is SalesforceRecordReadKind.Details or SalesforceRecordReadKind.Related ||
             proposal.Operation == SemanticOperation.Refine))
        {
            if (!TryGetSalesforceRecord(context, proposal.Ordinal, out var resolvedRecord, out var resultCount))
            {
                safeReason = resultCount > 1 && proposal.Ordinal is null
                    ? proposal.Operation == SemanticOperation.Refine
                        ? "That refinement needs one stable Salesforce record. Narrow the prior result first."
                        : "The prior Salesforce result contains multiple records. Specify a supported ordinal before asking for details or related records."
                    : proposal.Ordinal is not null && resultCount > 0
                        ? "That ordinal is not available in the grounded Salesforce result."
                        : "There is no grounded Salesforce record to reuse. Run a bounded Salesforce read first.";
                return false;
            }
            if (proposal.Operation == SemanticOperation.Refine && resultCount != 1)
            {
                safeReason = "That refinement needs one stable Salesforce record. Narrow the prior result first.";
                return false;
            }
            if (kind == SalesforceRecordReadKind.Related) relatedTo = resolvedRecord;
            else if (kind == SalesforceRecordReadKind.Details || proposal.Operation == SemanticOperation.Refine)
            {
                record = resolvedRecord;
                kind = SalesforceRecordReadKind.Details;
            }
        }
        request = new SalesforceRecordReadRequest(
            new SalesforceSemanticEntity(proposal.Entity!),
            kind,
            Filters: filters,
            Sorts: proposal.Sorts?.Select(static sort =>
                new SalesforceSort(new SalesforceSemanticField(sort.Field), sort.Direction)).ToArray(),
            Limit: proposal.Limit,
            Record: record,
            RelatedTo: relatedTo);
        return true;
    }

    private static bool TryCompileSalesforceAggregate(
        SemanticIntentProposal proposal,
        out SalesforceAggregateRequest request,
        out string safeReason)
    {
        request = default!;
        safeReason = "That Salesforce aggregate could not be compiled safely.";
        if (!ValidSemanticText(proposal.Entity, required: true) || proposal.Aggregate is null ||
            !TryCompileSalesforceFilters(proposal, out var filters))
            return false;
        request = new SalesforceAggregateRequest(
            new SalesforceSemanticEntity(proposal.Entity!),
            proposal.Aggregate.Function,
            proposal.Aggregate.Field is null ? null : new SalesforceSemanticField(proposal.Aggregate.Field),
            proposal.Aggregate.GroupBy is null ? null : new SalesforceSemanticField(proposal.Aggregate.GroupBy),
            filters,
            Math.Min(50, proposal.Limit));
        return true;
    }

    private static bool TryCompileSalesforceFilters(
        SemanticIntentProposal proposal,
        out IReadOnlyList<SalesforceFilter> filters)
    {
        var result = new List<SalesforceFilter>();
        foreach (var filter in proposal.Filters ?? [])
        {
            if (filter.Operator == SemanticFilterOperator.Set) { filters = []; return false; }
            if (NormalizeSemanticName(filter.Field) == "open")
            {
                result.Add(new SalesforceFilter(
                    new SalesforceSemanticField("Is Closed"),
                    SemanticFilterOperator.Equals,
                    "false"));
                continue;
            }
            result.Add(new SalesforceFilter(
                new SalesforceSemanticField(filter.Field),
                filter.Operator,
                filter.Value));
        }
        if (proposal.TimeRange != SemanticTimeRange.None)
        {
            if (!TryGetTimeRange(proposal.TimeRange, out var from, out var until)) { filters = []; return false; }
            result.Add(new SalesforceFilter(
                new SalesforceSemanticField("Close Date"),
                SemanticFilterOperator.GreaterThanOrEqual,
                from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            result.Add(new SalesforceFilter(
                new SalesforceSemanticField("Close Date"),
                SemanticFilterOperator.LessThan,
                until.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }
        filters = result;
        return true;
    }

    private ToolGrounding? LatestGrounding(RuntimeRequestContext context, string toolPrefix)
    {
        if (_conversations is null) return null;
        foreach (var operation in _conversations.Read(context).Operations.Reverse())
        {
            if (!string.Equals(operation.State, InoConversationStates.Succeeded, StringComparison.Ordinal)) continue;
            var groundings = operation.Groundings is { Count: > 0 }
                ? operation.Groundings
                : operation.Grounding is { } grounding
                    ? [grounding]
                    : [];
            var match = groundings.FirstOrDefault(value => value.ToolId.StartsWith(toolPrefix, StringComparison.Ordinal));
            if (match is not null) return match;
        }
        return null;
    }

    private static bool TryGetGmailSelection(JsonElement content, out GmailMessageSelection selection)
    {
        selection = default!;
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope) ||
            !envelope.TryGetProperty("selection", out var selectionElement))
            return false;
        try
        {
            selection = selectionElement.Deserialize<GmailMessageSelection>(SemanticJson)!;
            return selection is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GmailMessageIds(JsonElement content)
    {
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope)) yield break;
        if (envelope.TryGetProperty("resultMessageIds", out var resultIds) && resultIds.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in resultIds.EnumerateArray())
                if (value.ValueKind == JsonValueKind.String && ValidProviderIdentifier(value.GetString()))
                    yield return value.GetString()!;
            yield break;
        }
        if (envelope.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in messages.EnumerateArray())
                if (TryGetString(message, "messageId", out var id) && ValidProviderIdentifier(id)) yield return id!;
            yield break;
        }
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array) yield break;
        foreach (var thread in threads.EnumerateArray())
        {
            if (!thread.TryGetProperty("messages", out messages) || messages.ValueKind != JsonValueKind.Array) continue;
            foreach (var message in messages.EnumerateArray())
                if (TryGetString(message, "messageId", out var id) && ValidProviderIdentifier(id)) yield return id!;
        }
    }

    private static string? GmailSender(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return null;
        if (content.TryGetProperty("incomingMessage", out var legacy) &&
            TryGetString(legacy, "senderAddress", out var legacyAddress)) return legacyAddress;
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope)) return null;
        if (envelope.TryGetProperty("senderAddresses", out var senderAddresses) &&
            senderAddresses.ValueKind == JsonValueKind.Array)
            return senderAddresses.EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String)
                .Select(static value => value.GetString())
                .FirstOrDefault(static value => ValidProviderIdentifier(value));
        if (envelope.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            return messages.EnumerateArray().Select(static message =>
                TryGetString(message, "fromAddress", out var value) ? value : null)
                .FirstOrDefault(static value => ValidProviderIdentifier(value));
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array) return null;
        foreach (var thread in threads.EnumerateArray())
        {
            if (!thread.TryGetProperty("messages", out messages) || messages.ValueKind != JsonValueKind.Array) continue;
            var value = messages.EnumerateArray().Select(static message =>
                TryGetString(message, "fromAddress", out var address) ? address : null)
                .FirstOrDefault(static address => ValidProviderIdentifier(address));
            if (value is not null) return value;
        }
        return null;
    }

    private string? TryGetLatestGmailSender(RuntimeRequestContext context) =>
        LatestGrounding(context, "gmail.") is { } grounding ? GmailSender(grounding.Content) : null;

    private bool TryGetSalesforceContinuation(RuntimeRequestContext context, out string value)
    {
        value = string.Empty;
        var grounding = LatestGrounding(context, "salesforce.");
        if (grounding is null || !TryGetString(grounding.Content, "continuation", out var candidate) ||
            !Guid.TryParseExact(candidate, "N", out _)) return false;
        value = candidate!;
        return true;
    }

    private string? TryGetLatestSalesforceEntity(RuntimeRequestContext context)
    {
        var grounding = LatestGrounding(context, "salesforce.");
        return grounding is not null && TryGetString(grounding.Content, "entity", out var entity) &&
               ValidProviderIdentifier(entity)
            ? entity
            : null;
    }

    private bool TryGetSalesforceRecord(
        RuntimeRequestContext context,
        int? ordinal,
        out SalesforceResolvedRecord record,
        out int resultCount)
    {
        record = default!;
        resultCount = 0;
        var grounding = LatestGrounding(context, "salesforce.");
        if (grounding is null || grounding.Content.ValueKind != JsonValueKind.Object) return false;
        var entity = TryGetString(grounding.Content, "entity", out var entityValue) && ValidProviderIdentifier(entityValue)
            ? entityValue
            : null;
        var recordIds = SalesforceRecordIds(grounding.Content)
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();
        resultCount = TryGetInt32(grounding.Content, "resultCount", out var count)
            ? Math.Max(count, recordIds.Length)
            : recordIds.Length;
        var index = ordinal is { } requestedOrdinal ? requestedOrdinal - 1 : 0;
        if (index < 0 || index >= recordIds.Length || ordinal is null && resultCount != 1) return false;
        record = new SalesforceResolvedRecord(
            new SalesforceSemanticEntity(entity ?? "record"),
            recordIds[index]);
        return true;
    }

    private ToolOutcome SalesforceOutcome(
        SalesforceReadResult result,
        string resultField,
        string? entity,
        string? matchedSender = null)
    {
        if (result.Status != SalesforceReadStatus.Success)
            return SalesforceFailure(result);
        JsonElement content;
        try { content = JsonElement.Parse(result.Content ?? "[]"); }
        catch (JsonException) { content = JsonSerializer.SerializeToElement(result.Content ?? string.Empty); }
        var continuationValue = result.Continuation is { Value: var opaque } && Guid.TryParseExact(opaque, "N", out _)
            ? opaque
            : null;
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [resultField] = content,
            ["entity"] = entity,
            ["resultCount"] = result.ReturnedCount,
            ["hasMore"] = continuationValue is not null
        };
        if (matchedSender is not null) envelope["matchedGmailSender"] = matchedSender;
        var recordIds = SalesforceRecordIds(content).Distinct(StringComparer.Ordinal).Take(20).ToArray();
        var grounding = JsonSerializer.SerializeToElement(new
        {
            entity,
            recordIds,
            resultCount = result.ReturnedCount,
            hasMore = continuationValue is not null,
            continuation = continuationValue,
            matchedGmailSender = matchedSender
        }, SemanticJson);
        return new ToolOutcome(
            ToolOutcomeKind.Success,
            JsonSerializer.SerializeToElement(envelope, SemanticJson),
            GroundingContent: grounding);
    }

    private static IEnumerable<string> SalesforceRecordIds(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            if (ValidSalesforceRecordId(value.GetString()))
            {
                yield return value.GetString()!;
                yield break;
            }
            JsonElement parsed;
            try { parsed = JsonElement.Parse(value.GetString() ?? string.Empty); }
            catch (JsonException) { yield break; }
            foreach (var id in SalesforceRecordIds(parsed)) yield return id;
            yield break;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var id in SalesforceRecordIds(item))
                    yield return id;
            }
            yield break;
        }
        if (value.ValueKind != JsonValueKind.Object) yield break;
        if (value.TryGetProperty("RecordId", out var recordId) && recordId.ValueKind == JsonValueKind.String &&
            ValidSalesforceRecordId(recordId.GetString()))
            yield return recordId.GetString()!;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is "Fields" or "attributes" or "RecordId") continue;
            foreach (var id in SalesforceRecordIds(property.Value)) yield return id;
        }
    }

    private ToolOutcome SalesforceFailure(SalesforceReadResult result) => result.Status switch
    {
        SalesforceReadStatus.NeedsAuth when _actionPolicy.IsAllowedOpenUrl(
            "Connect Salesforce",
            result.ConnectionUrl) => new ToolOutcome(
            ToolOutcomeKind.NeedsAuth,
            SafeReason: SafeProviderReason(result.SafeReason, "Connect your Salesforce account to let INO read Salesforce."),
            Action: new ToolAction("openUrl", "Connect Salesforce", result.ConnectionUrl!)),
        SalesforceReadStatus.NeedsAuth => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: "Salesforce connection is unavailable right now."),
        SalesforceReadStatus.ConfigurationMissing => new ToolOutcome(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: SafeProviderReason(result.SafeReason, "Salesforce application configuration is missing.")),
        SalesforceReadStatus.AccessDenied => new ToolOutcome(
            ToolOutcomeKind.Denied,
            SafeReason: "Salesforce denied access to that object or field for the connected user."),
        SalesforceReadStatus.InvalidRequest => InvalidTypedRequest(
            SafeProviderReason(result.SafeReason, "That Salesforce request is not supported by the accessible schema.")),
        SalesforceReadStatus.ContinuationExpired => InvalidTypedRequest(
            "That Salesforce continuation expired. Run the bounded read again."),
        SalesforceReadStatus.LimitReached => InvalidTypedRequest(
            SafeProviderReason(result.SafeReason, "The Salesforce safety limit was reached. Narrow the request.")),
        _ => new ToolOutcome(
            ToolOutcomeKind.RetryableFailure,
            SafeReason: "I couldn’t read Salesforce right now. Please try again later.")
    };

    private static ToolOutcome InvalidTypedRequest(string safeReason) =>
        new(ToolOutcomeKind.PermanentFailure, SafeReason: SafeProviderReason(safeReason, "That typed request is unavailable."));

    private static bool TryGetProviderEnvelope(JsonElement content, string[] names, out JsonElement envelope)
    {
        envelope = default;
        if (content.ValueKind != JsonValueKind.Object) return false;
        foreach (var name in names)
            if (content.TryGetProperty(name, out envelope) && envelope.ValueKind == JsonValueKind.Object) return true;
        return false;
    }

    private static bool TryGetString(JsonElement value, string propertyName, out string? result)
    {
        result = null;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String) return false;
        result = property.GetString();
        return true;
    }

    private static bool TryGetInt32(JsonElement value, string propertyName, out int result)
    {
        result = 0;
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(propertyName, out var direct) && direct.TryGetInt32(out result)) return true;
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty(propertyName, out var nested) && nested.TryGetInt32(out result)) return true;
        return false;
    }

    private static bool TryGetBoolean(JsonElement value, string propertyName, out bool result)
    {
        result = false;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out var direct) &&
            direct.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = direct.GetBoolean();
            return true;
        }
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty(propertyName, out var nested) ||
                nested.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            result = nested.GetBoolean();
            return true;
        }
        return false;
    }

    private static bool TryGetTimeRange(
        SemanticTimeRange range,
        out DateTimeOffset from,
        out DateTimeOffset until)
    {
        var now = DateTimeOffset.UtcNow;
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var quarterMonth = ((today.Month - 1) / 3) * 3 + 1;
        var quarter = new DateTimeOffset(today.Year, quarterMonth, 1, 0, 0, 0, TimeSpan.Zero);
        (from, until) = range switch
        {
            SemanticTimeRange.Today => (today, today.AddDays(1)),
            SemanticTimeRange.Yesterday => (today.AddDays(-1), today),
            SemanticTimeRange.CurrentWeek => (week, week.AddDays(7)),
            SemanticTimeRange.PreviousWeek => (week.AddDays(-7), week),
            SemanticTimeRange.CurrentMonth => (new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)),
            SemanticTimeRange.PreviousMonth => (new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
                new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero)),
            SemanticTimeRange.CurrentQuarter => (quarter, quarter.AddMonths(3)),
            SemanticTimeRange.PreviousQuarter => (quarter.AddMonths(-3), quarter),
            SemanticTimeRange.CurrentYear => (new DateTimeOffset(today.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(today.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            _ => (default, default)
        };
        return range != SemanticTimeRange.None;
    }

    private static bool IsSemanticTool(string toolId) => toolId is
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

    private static bool TryParseSemanticIntent(
        string toolId,
        JsonElement input,
        out SemanticIntentProposal proposal)
    {
        proposal = default!;
        if (input.ValueKind != JsonValueKind.Object || input.GetRawText().Length > 16 * 1024) return false;
        try { proposal = input.Deserialize<SemanticIntentProposal>(SemanticJson)!; }
        catch (JsonException) { return false; }
        if (proposal is null || proposal.Limit is < 1 or > GmailTools.MaximumResultCount ||
            proposal.Ordinal is < 1 or > GmailTools.MaximumResultCount ||
            proposal.Filters is { Count: > 8 } || proposal.Sorts is { Count: > 8 } ||
            !ValidSemanticText(proposal.Entity, required: false) ||
            !ValidSemanticText(proposal.SearchText, required: false) ||
            !ValidSemanticText(proposal.Clarification, required: false) ||
            proposal.Filters?.Any(static filter =>
                !ValidSemanticText(filter.Field, required: true) || !ValidSemanticText(filter.Value, required: false)) == true ||
            proposal.Sorts?.Any(static sort => !ValidSemanticText(sort.Field, required: true)) == true ||
            (proposal.Aggregate is { } aggregate &&
             (!ValidSemanticText(aggregate.Field, required: false) ||
              !ValidSemanticText(aggregate.GroupBy, required: false))))
            return false;
        return string.Equals(ExpectedSemanticTool(proposal), toolId, StringComparison.Ordinal);
    }

    private static string? ExpectedSemanticTool(SemanticIntentProposal proposal) => proposal.Provider switch
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

    private static bool ValidSemanticText(string? value, bool required) =>
        value is null ? !required : value.Trim().Length is > 0 and <= MaximumSemanticText && !value.Any(char.IsControl);

    private static bool ValidProviderIdentifier(string? value) =>
        value is { Length: > 0 and <= MaximumSemanticText } && !value.Any(char.IsControl);

    private static bool ValidSalesforceRecordId(string? value) =>
        value is { Length: 15 or 18 } && value.All(char.IsLetterOrDigit);

    private static string NormalizeSemanticName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsAllAccessible(string? entity) =>
        string.IsNullOrWhiteSpace(entity) || NormalizeSemanticName(entity) is "all" or "allaccessible";

    private static string SafeProviderReason(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) ||
            value.Contains("://", StringComparison.Ordinal)) return fallback;
        return value.Trim();
    }

    private static JsonSerializerOptions CreateSemanticJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string GmailMailboxStatus(GmailMailboxState state) => state switch
    {
        GmailMailboxState.SenderAvailable => "senderAvailable",
        GmailMailboxState.EmptyInbox => "emptyInbox",
        GmailMailboxState.SenderUnavailable => "senderUnavailable",
        _ => "positionUnavailable"
    };

    private static bool TryParseGmailRequest(JsonElement input, out GmailReadRequest request)
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
                     request.AnchorMessageId.Length is > 0 and <= 256);
    }

}
