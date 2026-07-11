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

public sealed class McpResponseComposer : IResponseSurfaceComposer
{
    private const string UngroundedMailboxReason = "I couldn’t verify that mailbox claim from a successful Gmail result, so I won’t guess.";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex EmailAddress = new(
        @"(?<![\p{L}\p{N}._%+-])[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+(?![\p{L}\p{N}._%+-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MailboxSenderClaim = new(
        @"\b(?:gmail|email|mailbox|incoming message)\b.{0,120}\b(?:sent by|sender|from)\b|" +
        @"\b(?:sent by|sender)\b.{0,120}\b(?:gmail|email|mailbox|message)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MailboxReference = new(
        @"\b(?:gmail|email|mailbox|incoming message|sender)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex UnsafeAddress = new(
        @"\b[a-z][a-z0-9+.-]*://|\bwww\.|(?<![\p{L}\p{N}_/@.-])(?:[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})(?::\d{2,5})?(?![\p{L}\p{N}_-])|" +
        @"(?<![\p{L}\p{N}.-])(?=[a-z0-9.-]*[a-z])(?:[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?):\d{2,5}(?!\d)|" +
        @"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?\b|" +
        @"(?<![\p{L}\p{N}:])(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}(?![\p{L}\p{N}:])|" +
        @"(?<!\\)\\\\[a-z0-9._$-]+(?:\\[^\s\\/:*?""<>|]+)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex UnsafeTerm = new(
        @"\b(?:idempotenc(?:y|e)|tenant|principal|grants?|tokens?|endpoints?|urls?|infrastructure|grpc|v2|secrets?|bearer)\b|" +
        @"\bfeed[\s_-]*metadata\b|\bsurface[\s_-]*(?:feed|revision)\b|\bfeed[\s_-]*sequence\b|\bwatchsurfacefeed\b|" +
        @"\b(?:operation|tenant|workspace|principal|binding)[\s_-]*(?:id|identifier)\b|\bapi[\s_-]*key\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public Task<string> ComposeAsync(
        RuntimeRequestContext context,
        ModelResponse response,
        IReadOnlyList<ToolOutcome> toolOutcomes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blockingOutcome = toolOutcomes.FirstOrDefault(static outcome => outcome.Kind != ToolOutcomeKind.Success);
        if (blockingOutcome is not null)
            return Task.FromResult(blockingOutcome.SafeReason ?? "I couldn’t complete that request safely.");
        var typedGmailResponse = toolOutcomes
            .Select(static outcome => ComposeGmailMetadata(outcome.Content))
            .FirstOrDefault(static text => text is not null);
        if (typedGmailResponse is not null)
            return Task.FromResult(typedGmailResponse);
        var groundedGmailResponse = toolOutcomes
            .Select(static outcome => ComposeIncomingGmail(outcome.Content))
            .FirstOrDefault(static text => text is not null);
        if (groundedGmailResponse is not null)
            return Task.FromResult(groundedGmailResponse);
        var groundedSalesforceResponse = toolOutcomes
            .Select(static outcome => ComposeSalesforce(outcome.Content))
            .FirstOrDefault(static text => text is not null);
        if (groundedSalesforceResponse is not null)
            return Task.FromResult(groundedSalesforceResponse);
        if (string.IsNullOrWhiteSpace(response.Text))
            throw new InvalidOperationException("The configured model returned no answer.");
        var text = response.Text.Trim();
        if (MailboxSenderClaim.IsMatch(text) ||
            (EmailAddress.IsMatch(text) && MailboxReference.IsMatch(text)))
            return Task.FromResult(UngroundedMailboxReason);
        if (UnsafeAddress.IsMatch(text) || UnsafeTerm.IsMatch(text) || ContainsSensitiveContextValue(text, context))
            throw new InvalidOperationException("The configured model returned an answer that is unsafe to display.");
        return Task.FromResult(text);
    }

    private static string? ComposeGmailMetadata(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } root) return null;
        if (root.TryGetProperty("gmailMailboxOverview", out var overview) && overview.ValueKind == JsonValueKind.Object)
        {
            return "Gmail mailbox overview: " +
                   $"{ReadBoundedInt(overview, "inboxMessages")} inbox messages, " +
                   $"{ReadBoundedInt(overview, "unreadInboxMessages")} unread, " +
                   $"{ReadBoundedInt(overview, "inboxThreads")} inbox threads, and " +
                   $"{ReadBoundedInt(overview, "unreadInboxThreads")} unread threads.";
        }
        if (root.TryGetProperty("gmailMessages", out var messageEnvelope) &&
            messageEnvelope.ValueKind == JsonValueKind.Object)
            return ComposeGmailMessages(messageEnvelope);
        if (root.TryGetProperty("gmailThreads", out var threadEnvelope) &&
            threadEnvelope.ValueKind == JsonValueKind.Object)
            return ComposeGmailThreads(threadEnvelope);
        return null;
    }

    private static string ComposeGmailMessages(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The Gmail metadata tool returned an invalid message list.");
        var rows = messages.EnumerateArray().Take(GmailTools.MaximumResultCount).Select((message, index) =>
        {
            var subject = ReadProviderString(message, "subject") ?? "(no subject)";
            var sender = ReadProviderString(message, "from") ??
                         ReadProviderString(message, "fromAddress") ?? "sender unavailable";
            var date = ReadGmailDate(message);
            var readState = message.TryGetProperty("isRead", out var read) && read.ValueKind == JsonValueKind.True
                ? "read"
                : "unread";
            return $"{index + 1}. Subject: “{SafeProviderText(subject, 180)}” — from {SafeProviderText(sender, 220)}; {date}; {readState}.";
        }).ToArray();
        if (rows.Length == 0) return "No matching Gmail messages were found within the bounded metadata read.";
        return "Gmail messages:\n" + string.Join("\n", rows) + CoverageNote(envelope);
    }

    private static string ComposeGmailThreads(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The Gmail metadata tool returned an invalid thread list.");
        var rows = threads.EnumerateArray().Take(GmailTools.MaximumResultCount).Select((thread, index) =>
        {
            var subject = ReadProviderString(thread, "subject") ?? "(no subject)";
            var count = ReadBoundedInt(thread, "matchingMessageCount");
            var unread = thread.TryGetProperty("hasUnread", out var unreadElement) && unreadElement.ValueKind == JsonValueKind.True
                ? "; has unread mail"
                : string.Empty;
            var participants = thread.TryGetProperty("participantAddresses", out var values) && values.ValueKind == JsonValueKind.Array
                ? string.Join(", ", values.EnumerateArray().Take(6)
                    .Where(static value => value.ValueKind == JsonValueKind.String)
                    .Select(static value => SafeProviderText(value.GetString() ?? string.Empty, 120)))
                : "participants unavailable";
            return $"{index + 1}. Thread: “{SafeProviderText(subject, 180)}” — {count} matching message(s); {participants}{unread}.";
        }).ToArray();
        if (rows.Length == 0) return "No matching Gmail threads were found within the bounded metadata read.";
        return "Gmail threads:\n" + string.Join("\n", rows) + CoverageNote(envelope);
    }

    private static string CoverageNote(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("coverage", out var coverage) || coverage.ValueKind != JsonValueKind.Object)
            return string.Empty;
        var limited = coverage.TryGetProperty("candidateLimitReached", out var candidateLimit) &&
                      candidateLimit.ValueKind == JsonValueKind.True;
        var exhausted = coverage.TryGetProperty("providerExhausted", out var providerExhausted) &&
                        providerExhausted.ValueKind == JsonValueKind.True;
        return limited || !exhausted
            ? "\nThis is a bounded partial result; narrow the request to search more precisely."
            : string.Empty;
    }

    private static string ReadGmailDate(JsonElement message)
    {
        if (!message.TryGetProperty("internalDate", out var value) || !value.TryGetInt64(out var milliseconds))
            return "date unavailable";
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return "date unavailable"; }
    }

    private static string? ReadProviderString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int ReadBoundedInt(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var number) && number >= 0
            ? number
            : 0;

    private static string? ComposeIncomingGmail(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("incomingMessage", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("status", out var statusElement) ||
            statusElement.ValueKind != JsonValueKind.String)
            return null;

        return statusElement.GetString() switch
        {
            "emptyInbox" => "No incoming Gmail messages were found.",
            "positionUnavailable" => "I couldn’t safely resolve that incoming Gmail position. Ask for the latest incoming email to start again.",
            "senderUnavailable" => ComposeUnavailableSender(message),
            "senderAvailable" => ComposeAvailableSender(message),
            _ => throw new InvalidOperationException("The Gmail tool returned an unknown mailbox state.")
        };
    }

    private static string ComposeAvailableSender(JsonElement message)
    {
        var sender = message.TryGetProperty("sender", out var senderElement) && senderElement.ValueKind == JsonValueKind.String
            ? senderElement.GetString()
            : null;
        var address = message.TryGetProperty("senderAddress", out var addressElement) && addressElement.ValueKind == JsonValueKind.String
            ? addressElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(address) ||
            !sender.Contains(address, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Gmail tool returned incomplete sender metadata.");
        var response = $"{PositionPrefix(message)} was sent by {sender}.";
        if (UnsafeAddress.IsMatch(response))
            throw new InvalidOperationException("The Gmail tool returned unsafe sender metadata.");
        return response;
    }

    private static string ComposeUnavailableSender(JsonElement message) =>
        $"{PositionPrefix(message)}’s sender metadata was unavailable.";

    private static string PositionPrefix(JsonElement message)
    {
        var anchored = message.TryGetProperty("anchoredPrevious", out var anchoredElement) &&
                       anchoredElement.ValueKind is JsonValueKind.True;
        if (anchored) return "The incoming email immediately before that";
        var depth = message.TryGetProperty("traversalDepth", out var depthElement) && depthElement.TryGetInt32(out var value)
            ? value
            : 0;
        return depth switch
        {
            0 => "The latest incoming email",
            1 => "The second-to-last incoming email",
            2 => "The third-to-last incoming email",
            3 => "The fourth-to-last incoming email",
            4 => "The fifth-to-last incoming email",
            _ => throw new InvalidOperationException("The Gmail tool returned an invalid traversal depth.")
        };
    }

    private static string? ComposeSalesforce(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } root) return null;
        foreach (var property in root.EnumerateObject())
        {
            var title = property.Name switch
            {
                "latestAccount" => "Latest Salesforce account",
                "recentAccounts" => "Salesforce accounts",
                "recentContacts" => "Salesforce contacts",
                "currentProfile" => "Salesforce profile",
                "crmSchema" => "Accessible Salesforce schema",
                "salesforceRecords" => "Salesforce records",
                "salesforceSearch" => "Salesforce search results",
                "salesforceAggregate" => "Salesforce aggregate",
                "salesforceObjects" => "Accessible Salesforce objects",
                "salesforceMutationPreview" => "Salesforce mutation preview (no change made)",
                _ => null
            };
            if (title is null) continue;
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.String)
            {
                var raw = value.GetString() ?? string.Empty;
                if (raw.Length > 64 * 1024)
                    throw new InvalidOperationException("The Salesforce tool returned an oversized result.");
                try { value = JsonElement.Parse(raw); }
                catch (JsonException) { return title + ": " + SafeProviderText(raw, 512); }
            }
            return title + ":\n" + FormatProviderValue(value, depth: 0);
        }
        return null;
    }

    private static string FormatProviderValue(JsonElement value, int depth)
    {
        if (depth > 4) return "[nested value omitted]";
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.GetArrayLength() == 0
                ? "No matching records."
                : string.Join("\n", value.EnumerateArray().Take(10).Select((item, index) =>
                    $"{index + 1}. {FormatProviderValue(item, depth + 1)}")),
            JsonValueKind.Object => string.Join("; ", value.EnumerateObject()
                .Where(static property => !HiddenSalesforceField(property.Name))
                .Take(24)
                .Select(property => $"{SafeProviderText(property.Name, 80)}: {FormatProviderValue(property.Value, depth + 1)}")),
            JsonValueKind.String => SafeProviderText(value.GetString() ?? string.Empty, 512),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "—",
            _ => "—"
        };
    }

    private static bool HiddenSalesforceField(string name) =>
        string.Equals(name, "attributes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "nextRecordsUrl", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase);

    private static string SafeProviderText(string value, int maximumLength)
    {
        var normalized = new string(value.Select(static character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        if (UnsafeAddress.IsMatch(normalized)) normalized = "[external address omitted]";
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength] + "…";
    }

    private static bool ContainsSensitiveContextValue(string text, RuntimeRequestContext context) =>
        ContainsScopeValue(text, "tenant", context.TenantId.Value) ||
        ContainsScopeValue(text, "workspace", context.WorkspaceId.Value) ||
        ContainsScopeValue(text, "principal", context.Principal.Value) ||
        context.Grants.Any(grant => ContainsDistinctIdentifier(text, grant));

    private static bool ContainsScopeValue(string text, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length >= 8 && ContainsDistinctIdentifier(text, value)) return true;
        return Regex.IsMatch(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){label}(?:[\s_-]+(?:id|identifier))?" +
            $@"(?:(?:\s*[:=#]\s*|\s+(?:is|equals?|named)\s+)(?:['""`\(\[]\s*)?|\s+['""`\(\[]\s*)" +
            DistinctIdentifierPattern(value),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
    }

    private static bool ContainsDistinctIdentifier(string text, string value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(
            text,
            DistinctIdentifierPattern(value),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);

    private static string DistinctIdentifierPattern(string value) =>
        $@"(?<![\p{{L}}\p{{N}}_/+%-])(?<![\p{{L}}\p{{N}}_][.:]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}_/+%-]|[.:](?=[\p{{L}}\p{{N}}_]))";
}
