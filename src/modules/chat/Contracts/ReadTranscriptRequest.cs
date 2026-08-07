using System.ComponentModel;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

internal static class ChatIdentity
{
    private const char OwnerNameSeparator = '/';

    internal static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();
        if (trimmed.Contains(OwnerNameSeparator, StringComparison.Ordinal) || trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"A conversation name cannot contain '{OwnerNameSeparator}' or whitespace; "
                + $"'{value}' is not addressable.",
                parameterName);
        }

        return trimmed;
    }
}

[GenerateSerializer]
[Alias("chat.read-transcript-request")]
[Description(
    "Returns the durable transcript kept for one named conversation, "
    + "optionally narrowed to recent entries")]
public sealed record ReadTranscriptRequest : RequestSynapse<TranscriptRead>
{
    public const int MinimumMaxTurns = 1;
    public const int MaximumMaxTurns = 64;

    public ReadTranscriptRequest(string chatName)
        : this(chatName, maxTurns: null, CommandId.New())
    {
    }

    public ReadTranscriptRequest(string chatName, int? maxTurns, CommandId commandId)
    {
        if (maxTurns is { } bound && (bound < MinimumMaxTurns || bound > MaximumMaxTurns))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTurns),
                maxTurns,
                $"A transcript cap holds between {MinimumMaxTurns} and {MaximumMaxTurns} turns.");
        }

        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        ChatName = ChatIdentity.Validated(chatName, nameof(chatName));
        MaxTurns = maxTurns;
        CommandId = commandId;
    }

    [JsonConstructor]
    private ReadTranscriptRequest(string chatName, CommandId commandId, int? maxTurns = null)
        : this(chatName, maxTurns, commandId)
    {
    }

    [Id(0)]
    [Description("Name of the addressed instance to read, for example 'main'")]
    public string ChatName { get; init; }

    [Id(1)]
    [Description("Most recent turns to return, from 1 through 64; omit for the full retained record")]
    public int? MaxTurns { get; init; }

    [Id(2)]
    public CommandId CommandId { get; init; }
}
