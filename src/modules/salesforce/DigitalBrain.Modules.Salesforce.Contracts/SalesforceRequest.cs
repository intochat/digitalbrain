using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.request")]
[Description("Intent-level Salesforce request; provider tools stay inside SalesforceModule")]
public sealed record SalesforceRequest : RequestSynapse<SalesforceResponse>
{
    public SalesforceRequest(string intent)
        : this(intent, CommandId.New(), null, null)
    {
    }

    public SalesforceRequest(string intent, CommandId commandId)
        : this(intent, commandId, null, null)
    {
    }

    public SalesforceRequest(
        string intent,
        CommandId commandId,
        string? accountId,
        string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        Intent = intent.Trim();
        CommandId = commandId;
        AccountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    [Id(0)]
    public string Intent { get; init; }

    [Id(1)]
    public CommandId CommandId { get; init; }

    [Id(2)]
    public string? AccountId { get; init; }

    [Id(3)]
    public string? Description { get; init; }

    public bool IsAccountDescriptionProposal =>
        AccountId is not null && Description is not null;
}
