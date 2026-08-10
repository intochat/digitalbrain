using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-get-message-response")]
public sealed record GmailGetMessageResponse : Synapse
{
    public GmailGetMessageResponse(
        CommandId commandId,
        GmailMessage? message,
        string? error = null)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        CommandId = commandId;
        Message = message;
        Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public GmailMessage? Message { get; init; }

    [Id(2)]
    public string? Error { get; init; }

    public bool Succeeded => Error is null;
}
