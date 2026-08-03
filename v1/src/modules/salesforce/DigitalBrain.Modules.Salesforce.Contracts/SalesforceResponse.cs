using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.response")]
[Description("Bounded typed Salesforce result for an intent or approval request")]
public sealed record SalesforceResponse : Synapse
{
    public SalesforceResponse(
        CommandId commandId,
        string intent,
        SalesforceAccountDescriptionMutation? mutation = null,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        CommandId = commandId;
        Intent = intent.Trim();
        Mutation = mutation;
        Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public string Intent { get; init; }

    [Id(2)]
    public SalesforceAccountDescriptionMutation? Mutation { get; init; }

    [Id(3)]
    public string? Error { get; init; }

    public bool Succeeded => Error is null;
}
