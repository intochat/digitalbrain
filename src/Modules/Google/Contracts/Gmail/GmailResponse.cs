using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-response")]
public sealed record GmailResponse : Synapse
{
    public GmailResponse(
        CommandId commandId,
        string intent,
        IReadOnlyList<GmailMessage> messages,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentNullException.ThrowIfNull(messages);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        CommandId = commandId;
        Intent = intent.Trim();
        Messages = messages;
        Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public string Intent { get; init; }

    [Id(2)]
    public IReadOnlyList<GmailMessage> Messages { get; init; }

    [Id(3)]
    public string? Error { get; init; }

    public bool Succeeded => Error is null;
}
