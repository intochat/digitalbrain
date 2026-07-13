using Orleans;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Runtime;

public enum SemanticProvider
{
    None,
    Gmail,
    Salesforce,
    CrossProvider,
    Ambiguous
}

public enum SemanticOperation
{
    Answer,
    Clarify,
    List,
    Overview,
    Threads,
    Summarize,
    Refine,
    Previous,
    Discover,
    Search,
    Related,
    Details,
    Aggregate,
    NextPage,
    Match,
    MutationPreview,
    MutationConfirm,
    QueryLanguage,
    Delete
}

public enum SemanticReference
{
    None,
    LatestProviderResult,
    SameSender,
    SameAccount,
    LatestGmailSender
}

public enum SemanticFilterOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    IsNull,
    IsNotNull,
    Set
}

public enum SemanticSortDirection
{
    Ascending,
    Descending
}

public enum SemanticAggregateFunction
{
    Count,
    CountDistinct,
    Sum,
    Average,
    Minimum,
    Maximum
}

public enum SemanticTimeRange
{
    None,
    Today,
    Yesterday,
    CurrentWeek,
    PreviousWeek,
    CurrentMonth,
    PreviousMonth,
    CurrentQuarter,
    PreviousQuarter,
    CurrentYear
}

[GenerateSerializer, Alias("digitalbrain.v2.semantic-filter")]
public sealed record SemanticFilter(
    [property: Id(0)] string Field,
    [property: Id(1)] SemanticFilterOperator Operator,
    [property: Id(2)] string? Value = null);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-sort")]
public sealed record SemanticSort(
    [property: Id(0)] string Field,
    [property: Id(1)] SemanticSortDirection Direction);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-aggregate")]
public sealed record SemanticAggregate(
    [property: Id(0)] SemanticAggregateFunction Function,
    [property: Id(1)] string? Field = null,
    [property: Id(2)] string? GroupBy = null);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-intent-proposal")]
public sealed record SemanticIntentProposal(
    [property: Id(0)] SemanticProvider Provider,
    [property: Id(1)] SemanticOperation Operation,
    [property: Id(2)] string? Entity = null,
    [property: Id(3)] int Limit = 1,
    [property: Id(4)] int? Ordinal = null,
    [property: Id(5)] SemanticReference Reference = SemanticReference.None,
    [property: Id(6)] IReadOnlyList<SemanticFilter>? Filters = null,
    [property: Id(7)] IReadOnlyList<SemanticSort>? Sorts = null,
    [property: Id(8)] SemanticAggregate? Aggregate = null,
    [property: Id(9)] SemanticTimeRange TimeRange = SemanticTimeRange.None,
    [property: Id(10)] string? SearchText = null,
    [property: Id(11)] string? Clarification = null,
    [property: Id(12)] int? RelativeDays = null);

[GenerateSerializer, Alias("digitalbrain.v2.grounding-descriptor")]
public sealed record GroundingDescriptor(
    [property: Id(0)] string Provider,
    [property: Id(1)] string ToolId,
    [property: Id(2)] int ResultCount,
    [property: Id(3)] bool HasContinuation,
    [property: Id(4)] int TurnDistance);

[GenerateSerializer, Alias("digitalbrain.v3.semantic-intent-request")]
public sealed record SemanticIntentRequest(
    [property: Id(0)] BrainOwnerId OwnerId,
    [property: Id(1)] ActorId ActorId,
    [property: Id(2)] string ConversationId,
    [property: Id(3)] string Prompt,
    [property: Id(4)] IReadOnlyList<GroundingDescriptor> Groundings);

public static class AssistantTools
{
    public const string Clarify = "assistant.clarify";
}

public static class CrossProviderTools
{
    public const string MatchSalesforceAccountToGmailSender = "cross.gmail-sender.salesforce-account.match";
}
