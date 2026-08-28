namespace DigitalBrain.Integrations.Fakes;

// These are schema-only DTOs for MCP discovery. Runtime responses remain JsonElement so the
// fake transports stay contract-light and can mirror provider payloads without a model catalog.
internal sealed record GmailMcpOutput(
    GmailThreadOutput[]? Threads = null,
    string? ResultCountEstimate = null,
    string? Id = null,
    string? ThreadId = null,
    string? Subject = null,
    string? Sender = null,
    string? PlaintextBody = null,
    string[]? ToRecipients = null,
    string[]? CcRecipients = null,
    string? Date = null,
    string[]? LabelIds = null,
    GmailMessageOutput[]? Messages = null,
    GmailLabelOutput[]? Labels = null,
    GmailDraftOutput[]? Drafts = null,
    bool? Success = null);

internal sealed record GmailThreadOutput(string Id, GmailMessageOutput[] Messages);

internal sealed record GmailMessageOutput(
    string Id,
    string? ThreadId = null,
    string? Snippet = null,
    string? Subject = null,
    string? Sender = null,
    string? PlaintextBody = null,
    string[]? ToRecipients = null,
    string[]? CcRecipients = null,
    string? Date = null,
    string[]? LabelIds = null);

internal sealed record GmailLabelOutput(string Id, string Name, string Type);

internal sealed record GmailDraftOutput(
    string Id,
    string? ThreadId = null,
    string? Subject = null,
    string? PlaintextBody = null,
    string[]? ToRecipients = null,
    string? Date = null);

internal sealed record SalesforceMcpOutput(
    SalesforceObjectOutput[]? Objects = null,
    int? TotalSize = null,
    bool? Done = null,
    SalesforceRecordOutput[]? Records = null,
    SalesforceRecordOutput[]? SearchRecords = null,
    string? Id = null,
    bool? Success = null,
    bool? Created = null,
    string? SobjectName = null,
    SalesforceRecordOutput? Body = null);

internal sealed record SalesforceObjectOutput(string Name, string Description);

internal sealed record SalesforceRecordOutput(
    string? Id = null,
    string? Name = null,
    string? Website = null,
    string? Description = null,
    bool? DescriptionVerified = null);
