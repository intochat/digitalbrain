using System.ComponentModel;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

// NeuronId rejects separators and whitespace by throwing. A model-supplied behavior identity must
// fail here, while it is still a request-validation error, rather than inside a handler where the
// throw becomes an undeliverable synapse the outbox retries for its whole horizon.
internal static class BehaviorAuthoringIdentity
{
    private const char GrainKeySeparator = '/';

    internal static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();
        if (trimmed.Contains(GrainKeySeparator, StringComparison.Ordinal) || trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"A behavior identity cannot contain '{GrainKeySeparator}' or whitespace; "
                + $"'{value}' is not addressable.",
                parameterName);
        }

        return trimmed;
    }
}

public static class BehaviorChangeStatus
{
    public const string AwaitingScenarioApproval = "awaiting-scenario-approval";
    public const string Rejected = "rejected";
}

[GenerateSerializer]
[Alias("ai.behavior-change-proposal")]
[Description("A drafted behavior change: the scenarios proposed for it and how it differs from today")]
public sealed record BehaviorChangeProposal(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string BehaviorId,
    [property: Id(2)] string RequestText,
    [property: Id(3)] string ProposedFeatureText,
    [property: Id(4)] string ProposedFeatureName,
    [property: Id(5)] string Status,
    [property: Id(6)] string? DiffSummary);

[GenerateSerializer]
[Alias("ai.propose-behavior-change-request")]
[Description(
    "Drafts a scenario-first change to one of the owner's behaviors: returns the proposed feature "
    + "scenarios and a diff summary for the owner to approve, and writes no program source")]
public sealed record ProposeBehaviorChangeRequest : RequestSynapse<BehaviorChangeProposed>
{
    public ProposeBehaviorChangeRequest(string behaviorId, string requestText)
        : this(behaviorId, requestText, CommandId.New())
    {
    }

    [JsonConstructor]
    public ProposeBehaviorChangeRequest(string behaviorId, string requestText, CommandId commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestText);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        BehaviorId = BehaviorAuthoringIdentity.Validated(behaviorId, nameof(behaviorId));
        RequestText = requestText.Trim();
        CommandId = commandId;
    }

    [Id(0)]
    [Description("Identity of the behavior to change, for example 'com.digitalbrain.account-enrichment'")]
    public string BehaviorId { get; init; }

    [Id(1)]
    [Description("What the owner wants the behavior to do differently, in plain words")]
    public string RequestText { get; init; }

    [Id(2)]
    public CommandId CommandId { get; init; }
}

[GenerateSerializer]
[Alias("ai.behavior-change-proposed")]
[Description("The drafted behavior change awaiting the owner's scenario approval, or why it was refused")]
public sealed record BehaviorChangeProposed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] BehaviorChangeProposal? Proposal,
    [property: Id(2)] string? Error = null) : Synapse
{
    public bool Succeeded => Error is null;

    public static BehaviorChangeProposed Refused(CommandId commandId, string reason)
        => new(commandId, Proposal: null, reason);
}

[GenerateSerializer]
[Alias("ai.approve-behavior-change")]
public sealed record ApproveBehaviorChange
{
    public ApproveBehaviorChange(
        CommandId commandId,
        string behaviorId,
        string proposalId,
        bool approved,
        string? featureText = null,
        string? featureName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        CommandId = commandId;
        BehaviorId = BehaviorAuthoringIdentity.Validated(behaviorId, nameof(behaviorId));
        ProposalId = proposalId.Trim();
        Approved = approved;
        FeatureText = featureText;
        FeatureName = featureName;
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public string BehaviorId { get; init; }

    [Id(2)]
    public string ProposalId { get; init; }

    [Id(3)]
    public bool Approved { get; init; }

    [Id(4)]
    public string? FeatureText { get; init; }

    [Id(5)]
    public string? FeatureName { get; init; }
}

[GenerateSerializer]
[Alias("ai.behavior-change-decision")]
public sealed record BehaviorChangeDecision(
    [property: Id(0)] BehaviorChangeProposal? Proposal,
    [property: Id(1)] bool Applied)
{
    public static BehaviorChangeDecision Unknown { get; } = new(Proposal: null, Applied: false);
}
