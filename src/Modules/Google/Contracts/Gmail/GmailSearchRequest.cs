using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.google.gmail-search-request")]
public sealed record GmailSearchRequest : RequestSynapse<GmailSearchResponse>
{
    public const int DefaultMaxResults = 10;
    public const int MinimumMaxResults = 1;
    public const int MaximumMaxResults = 10;

    public GmailSearchRequest(string query)
        : this(query, DefaultMaxResults, CommandId.New())
    {
    }

    public GmailSearchRequest(string query, CommandId commandId)
        : this(query, DefaultMaxResults, commandId)
    {
    }

    public GmailSearchRequest(string query, int maxResults)
        : this(query, maxResults, CommandId.New())
    {
    }

    public GmailSearchRequest(string query, int maxResults, CommandId commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < MinimumMaxResults or > MaximumMaxResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                maxResults,
                $"MaxResults must be between {MinimumMaxResults} and {MaximumMaxResults}.");
        }

        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        Query = query.Trim();
        MaxResults = maxResults;
        CommandId = commandId;
    }

    [JsonConstructor]
    public GmailSearchRequest(string query, CommandId commandId, int maxResults = DefaultMaxResults)
        : this(query, maxResults, commandId)
    {
    }

    [Id(0)]
    public string Query { get; init; }

    [Id(1)]
    public int MaxResults { get; init; }

    [Id(2)]
    public CommandId CommandId { get; init; }
}
