using System.ComponentModel;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-request")]
[Description("Intent-level Gmail request; provider tools stay inside GoogleModule")]
public sealed record GmailRequest : RequestSynapse<GmailResponse>
{
    public GmailRequest(string intent)
        : this(intent, CommandId.New())
    {
    }

    [JsonConstructor]
    public GmailRequest(string intent, CommandId commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        Intent = intent.Trim();
        CommandId = commandId;
    }

    [Id(0)]
    public string Intent { get; init; }

    [Id(1)]
    public CommandId CommandId { get; init; }
}
