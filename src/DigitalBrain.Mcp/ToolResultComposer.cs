using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Mcp;

internal sealed class ToolResultComposer(SemanticIntentValidator validator)
{
    internal ToolOutcome GmailIncoming(GmailReadResult result) => new(
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
        }));

    internal ToolOutcome GmailMailboxOverview(GmailMailboxOverviewResult overview)
    {
        var content = JsonSerializer.SerializeToElement(new
        {
            gmailMailboxOverview = new
            {
                overview.InboxMessages,
                overview.UnreadInboxMessages,
                overview.InboxThreads,
                overview.UnreadInboxThreads
            }
        }, validator.Json);
        return new ToolOutcome(
            ToolOutcomeKind.Success,
            content,
            GroundingContent: content.Clone());
    }

    internal ToolOutcome GmailMessages(GmailMessageListRequest request, GmailMessageListResult result)
    {
        var stableIds = (request.Selection.PinnedMessageIds ?? result.StableCandidateMessageIds ??
                         result.Messages.Select(static message => message.MessageId).ToArray())
            .Where(validator.IsValidProviderIdentifier)
            .Distinct(StringComparer.Ordinal)
            .Take(GmailTools.MaximumCandidateCount)
            .ToArray();
        var consumedCandidates = request.Selection.PinnedMessageIds is null
            ? result.Messages.Length
            : Math.Min(request.Limit, Math.Max(0, stableIds.Length - request.Offset));
        var nextOffset = checked(request.Offset + consumedCandidates);
        var hasMore = nextOffset < stableIds.Length;
        var stableSelection = request.Selection with { PinnedMessageIds = stableIds.Length == 0 ? null : stableIds };
        var display = JsonSerializer.SerializeToElement(new
        {
            gmailMessages = new
            {
                messages = result.Messages,
                coverage = result.Coverage,
                hasMore
            }
        }, validator.Json);
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
        }, validator.Json);
        return new ToolOutcome(
            ToolOutcomeKind.Success,
            display,
            GroundingContent: grounding);
    }

    internal ToolOutcome GmailThreads(GmailThreadListRequest request, GmailThreadListResult result)
    {
        var stableCandidateMessageIds = (result.StableCandidateMessageIds ?? result.Threads
                .SelectMany(static thread => thread.Messages)
                .Select(static message => message.MessageId).ToArray())
            .Where(validator.IsValidProviderIdentifier)
            .Distinct(StringComparer.Ordinal)
            .Take(GmailTools.MaximumCandidateCount)
            .ToArray();
        var nextOffset = checked(request.Offset + result.Threads.Length);
        var stableThreadIds = (result.StableCandidateThreadIds ?? result.Threads
                .Select(static thread => thread.ThreadId).ToArray())
            .Where(validator.IsValidProviderIdentifier)
            .Distinct(StringComparer.Ordinal)
            .Take(GmailTools.MaximumCandidateCount)
            .ToArray();
        var hasMore = nextOffset < stableThreadIds.Length;
        var stableSelection = request.Selection with
        {
            PinnedMessageIds = stableCandidateMessageIds.Length == 0 ? null : stableCandidateMessageIds
        };
        var display = JsonSerializer.SerializeToElement(new
        {
            gmailThreads = new
            {
                threads = result.Threads,
                coverage = result.Coverage,
                hasMore
            }
        }, validator.Json);
        var grounding = JsonSerializer.SerializeToElement(new
        {
            gmailThreads = new
            {
                resultMessageIds = result.Threads.SelectMany(static thread => thread.Messages)
                    .Select(static message => message.MessageId).ToArray(),
                threadIds = result.Threads.Select(static thread => thread.ThreadId).ToArray(),
                stableThreadIds,
                senderAddresses = result.Threads.SelectMany(static thread => thread.Messages)
                    .Select(static message => message.FromAddress)
                    .Where(static value => value is not null).ToArray(),
                selection = stableSelection,
                nextOffset,
                hasMore
            }
        }, validator.Json);
        return new ToolOutcome(
            ToolOutcomeKind.Success,
            display,
            GroundingContent: grounding);
    }

    internal ToolOutcome Salesforce(
        SalesforceReadResult result,
        string resultField,
        string? entity,
        string? matchedSender = null)
    {
        JsonElement content;
        try
        {
            content = JsonElement.Parse(result.Content ?? "[]");
        }
        catch (JsonException)
        {
            content = JsonSerializer.SerializeToElement(result.Content ?? string.Empty);
        }
        var continuationValue = result.Continuation is { Value: var opaque } &&
                                Guid.TryParseExact(opaque, "N", out _)
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
        var recordIds = validator.SalesforceRecordIds(content).Distinct(StringComparer.Ordinal).Take(20).ToArray();
        var grounding = JsonSerializer.SerializeToElement(new
        {
            entity,
            recordIds,
            resultCount = result.ReturnedCount,
            hasMore = continuationValue is not null,
            continuation = continuationValue,
            matchedGmailSender = matchedSender
        }, validator.Json);
        return new ToolOutcome(
            ToolOutcomeKind.Success,
            JsonSerializer.SerializeToElement(envelope, validator.Json),
            GroundingContent: grounding);
    }

    internal ToolOutcome SalesforceMutationPreview(SemanticIntentProposal proposal, SemanticFilter[] changes) => new(
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
        }, validator.Json));

    internal ToolOutcome InvalidTypedRequest(string safeReason) =>
        new(
            ToolOutcomeKind.PermanentFailure,
            SafeReason: validator.SafeProviderReason(safeReason, "That typed request is unavailable."));


    private static string GmailMailboxStatus(GmailMailboxState state) => state switch
    {
        GmailMailboxState.SenderAvailable => "senderAvailable",
        GmailMailboxState.EmptyInbox => "emptyInbox",
        GmailMailboxState.SenderUnavailable => "senderUnavailable",
        _ => "positionUnavailable"
    };
}
