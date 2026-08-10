using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-search-response")]
public sealed record GmailSearchResponse : Synapse
{
    public GmailSearchResponse(
        CommandId commandId,
        IReadOnlyList<GmailMessageHeader> headers,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        CommandId = commandId;
        Headers = headers;
        Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public IReadOnlyList<GmailMessageHeader> Headers { get; init; }

    [Id(2)]
    public string? Error { get; init; }

    public bool Succeeded => Error is null;
}
