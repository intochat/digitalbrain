using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-get-message-request")]
[Description("Read-only fetch of one Gmail message by id; body is bounded by the handler")]
public sealed record GmailGetMessageRequest : RequestSynapse<GmailGetMessageResponse>
{
    public GmailGetMessageRequest(string messageId)
        : this(messageId, CommandId.New())
    {
    }

    public GmailGetMessageRequest(string messageId, CommandId commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        MessageId = messageId.Trim();
        CommandId = commandId;
    }

    [Id(0)]
    public string MessageId { get; init; }

    [Id(1)]
    public CommandId CommandId { get; init; }
}
