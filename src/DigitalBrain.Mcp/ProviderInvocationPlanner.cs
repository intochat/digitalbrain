using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

internal sealed class ProviderInvocationPlanner(
    IInoConversationStore? conversations,
    SemanticIntentValidator validator)
{
    internal bool TryCompileGmail(
        SemanticIntentProposal proposal,
        InoConversationSnapshot? history,
        out GmailMessageSelection selection,
        out int offset,
        out string safeReason)
    {
        selection = GmailSelectionForEntity(proposal.Entity);
        offset = Math.Max(0, (proposal.Ordinal ?? 1) - 1);
        safeReason = "That Gmail selection could not be compiled safely.";

        if (proposal.Reference != SemanticReference.None)
        {
            var grounding = LatestGrounding(history, "gmail.");
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
                if (!validator.IsValidProviderIdentifier(sender) || !sender!.Contains('@', StringComparison.Ordinal))
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
            var field = validator.NormalizeName(filter.Field);
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
                    if (!validator.IsValidProviderIdentifier(value)) return false;
                    selection = selection with { SenderAddress = value };
                    break;
                case "recipient":
                case "to":
                    if (!validator.IsValidProviderIdentifier(value)) return false;
                    selection = selection with { RecipientAddress = value };
                    break;
                case "subject":
                    if (!validator.IsValidProviderIdentifier(value)) return false;
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

    internal bool TryCompileSalesforceRead(
        SemanticIntentProposal proposal,
        InoConversationSnapshot? history,
        out SalesforceRecordReadRequest request,
        out string safeReason)
    {
        request = default!;
        safeReason = "That Salesforce read could not be compiled safely.";
        if (!validator.IsValidText(proposal.Entity, required: true))
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
            if (!TryGetSalesforceRecord(history, proposal.Ordinal, out var resolvedRecord, out var resultCount))
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

    internal bool TryCompileSalesforceAggregate(
        SemanticIntentProposal proposal,
        out SalesforceAggregateRequest request,
        out string safeReason)
    {
        request = default!;
        safeReason = "That Salesforce aggregate could not be compiled safely.";
        if (!validator.IsValidText(proposal.Entity, required: true) || proposal.Aggregate is null ||
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

    internal string? TryGetLatestGmailSender(InoConversationSnapshot? history) =>
        LatestGrounding(history, "gmail.") is { } grounding ? GmailSender(grounding.Content) : null;

    internal bool TryGetSalesforceContinuation(InoConversationSnapshot? history, out string value)
    {
        value = string.Empty;
        var grounding = LatestGrounding(history, "salesforce.");
        if (grounding is null || !TryGetString(grounding.Content, "continuation", out var candidate) ||
            !Guid.TryParseExact(candidate, "N", out _)) return false;
        value = candidate!;
        return true;
    }

    internal string? TryGetLatestSalesforceEntity(InoConversationSnapshot? history)
    {
        var grounding = LatestGrounding(history, "salesforce.");
        return grounding is not null && TryGetString(grounding.Content, "entity", out var entity) &&
               validator.IsValidProviderIdentifier(entity)
            ? entity
            : null;
    }

    private GmailMessageSelection GmailSelectionForEntity(string? entity) =>
        validator.NormalizeName(entity ?? string.Empty) switch
        {
            "inbox" => new GmailMessageSelection(GmailMailboxScope.Inbox),
            "sent" or "sentmail" => new GmailMessageSelection(GmailMailboxScope.Sent),
            "draft" or "drafts" => new GmailMessageSelection(GmailMailboxScope.Drafts),
            "all" or "allmail" => new GmailMessageSelection(GmailMailboxScope.All),
            _ => new GmailMessageSelection(GmailMailboxScope.Incoming)
        };

    private bool TryCompileSalesforceFilters(
        SemanticIntentProposal proposal,
        out IReadOnlyList<SalesforceFilter> filters)
    {
        var result = new List<SalesforceFilter>();
        foreach (var filter in proposal.Filters ?? [])
        {
            if (filter.Operator == SemanticFilterOperator.Set)
            {
                filters = [];
                return false;
            }
            if (validator.NormalizeName(filter.Field) == "open")
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
            if (!TryGetTimeRange(proposal.TimeRange, out var from, out var until))
            {
                filters = [];
                return false;
            }
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

    internal async Task<InoConversationSnapshot?> ReadHistoryAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken)
    {
        return conversations is null
            ? null
            : await conversations.ReadAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static ToolGrounding? LatestGrounding(InoConversationSnapshot? history, string toolPrefix)
    {
        if (history is null) return null;
        foreach (var operation in history.Operations.Reverse())
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

    private bool TryGetGmailSelection(JsonElement content, out GmailMessageSelection selection)
    {
        selection = default!;
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope) ||
            !envelope.TryGetProperty("selection", out var selectionElement))
            return false;
        try
        {
            selection = selectionElement.Deserialize<GmailMessageSelection>(validator.Json)!;
            return selection is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private IEnumerable<string> GmailMessageIds(JsonElement content)
    {
        if (!TryGetProviderEnvelope(content, ["gmailMessages", "gmailThreads"], out var envelope)) yield break;
        if (envelope.TryGetProperty("resultMessageIds", out var resultIds) && resultIds.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in resultIds.EnumerateArray())
                if (value.ValueKind == JsonValueKind.String && validator.IsValidProviderIdentifier(value.GetString()))
                    yield return value.GetString()!;
            yield break;
        }
        if (envelope.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in messages.EnumerateArray())
                if (TryGetString(message, "messageId", out var id) && validator.IsValidProviderIdentifier(id))
                    yield return id!;
            yield break;
        }
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array) yield break;
        foreach (var thread in threads.EnumerateArray())
        {
            if (!thread.TryGetProperty("messages", out messages) || messages.ValueKind != JsonValueKind.Array) continue;
            foreach (var message in messages.EnumerateArray())
                if (TryGetString(message, "messageId", out var id) && validator.IsValidProviderIdentifier(id))
                    yield return id!;
        }
    }

    private string? GmailSender(JsonElement content)
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
                .FirstOrDefault(validator.IsValidProviderIdentifier);
        if (envelope.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            return messages.EnumerateArray().Select(static message =>
                    TryGetString(message, "fromAddress", out var value) ? value : null)
                .FirstOrDefault(validator.IsValidProviderIdentifier);
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array) return null;
        foreach (var thread in threads.EnumerateArray())
        {
            if (!thread.TryGetProperty("messages", out messages) || messages.ValueKind != JsonValueKind.Array) continue;
            var value = messages.EnumerateArray().Select(static message =>
                    TryGetString(message, "fromAddress", out var address) ? address : null)
                .FirstOrDefault(validator.IsValidProviderIdentifier);
            if (value is not null) return value;
        }
        return null;
    }

    private bool TryGetSalesforceRecord(
        InoConversationSnapshot? history,
        int? ordinal,
        out SalesforceResolvedRecord record,
        out int resultCount)
    {
        record = default!;
        resultCount = 0;
        var grounding = LatestGrounding(history, "salesforce.");
        if (grounding is null || grounding.Content.ValueKind != JsonValueKind.Object) return false;
        var entity = TryGetString(grounding.Content, "entity", out var entityValue) &&
                     validator.IsValidProviderIdentifier(entityValue)
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

    private IEnumerable<string> SalesforceRecordIds(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            if (validator.IsValidSalesforceRecordId(value.GetString()))
            {
                yield return value.GetString()!;
                yield break;
            }
            JsonElement parsed;
            try
            {
                parsed = JsonElement.Parse(value.GetString() ?? string.Empty);
            }
            catch (JsonException)
            {
                yield break;
            }
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
            validator.IsValidSalesforceRecordId(recordId.GetString()))
            yield return recordId.GetString()!;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is "Fields" or "attributes" or "RecordId") continue;
            foreach (var id in SalesforceRecordIds(property.Value)) yield return id;
        }
    }

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
            SemanticTimeRange.CurrentMonth =>
                (new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)),
            SemanticTimeRange.PreviousMonth =>
                (new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1),
                    new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero)),
            SemanticTimeRange.CurrentQuarter => (quarter, quarter.AddMonths(3)),
            SemanticTimeRange.PreviousQuarter => (quarter.AddMonths(-3), quarter),
            SemanticTimeRange.CurrentYear =>
                (new DateTimeOffset(today.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(today.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            _ => (default, default)
        };
        return range != SemanticTimeRange.None;
    }
}
