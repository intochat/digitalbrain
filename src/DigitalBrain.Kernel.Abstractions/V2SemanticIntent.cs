using Orleans;

namespace DigitalBrain.Kernel.V2;

public enum V2SemanticProvider
{
    None,
    Gmail,
    Salesforce,
    CrossProvider,
    Ambiguous
}

public enum V2SemanticOperation
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

public enum V2SemanticReference
{
    None,
    LatestProviderResult,
    SameSender,
    SameAccount,
    LatestGmailSender
}

public enum V2SemanticFilterOperator
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

public enum V2SemanticSortDirection
{
    Ascending,
    Descending
}

public enum V2SemanticAggregateFunction
{
    Count,
    CountDistinct,
    Sum,
    Average,
    Minimum,
    Maximum
}

public enum V2SemanticTimeRange
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
public sealed record V2SemanticFilter(
    [property: Id(0)] string Field,
    [property: Id(1)] V2SemanticFilterOperator Operator,
    [property: Id(2)] string? Value = null);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-sort")]
public sealed record V2SemanticSort(
    [property: Id(0)] string Field,
    [property: Id(1)] V2SemanticSortDirection Direction);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-aggregate")]
public sealed record V2SemanticAggregate(
    [property: Id(0)] V2SemanticAggregateFunction Function,
    [property: Id(1)] string? Field = null,
    [property: Id(2)] string? GroupBy = null);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-intent-proposal")]
public sealed record V2SemanticIntentProposal(
    [property: Id(0)] V2SemanticProvider Provider,
    [property: Id(1)] V2SemanticOperation Operation,
    [property: Id(2)] string? Entity = null,
    [property: Id(3)] int Limit = 1,
    [property: Id(4)] int? Ordinal = null,
    [property: Id(5)] V2SemanticReference Reference = V2SemanticReference.None,
    [property: Id(6)] IReadOnlyList<V2SemanticFilter>? Filters = null,
    [property: Id(7)] IReadOnlyList<V2SemanticSort>? Sorts = null,
    [property: Id(8)] V2SemanticAggregate? Aggregate = null,
    [property: Id(9)] V2SemanticTimeRange TimeRange = V2SemanticTimeRange.None,
    [property: Id(10)] string? SearchText = null,
    [property: Id(11)] string? Clarification = null);

[GenerateSerializer, Alias("digitalbrain.v2.grounding-descriptor")]
public sealed record V2GroundingDescriptor(
    [property: Id(0)] string Provider,
    [property: Id(1)] string ToolId,
    [property: Id(2)] int ResultCount,
    [property: Id(3)] bool HasContinuation,
    [property: Id(4)] int TurnDistance);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-intent-request")]
public sealed record V2SemanticIntentRequest(
    [property: Id(0)] string TenantId,
    [property: Id(1)] string WorkspaceId,
    [property: Id(2)] string ConversationId,
    [property: Id(3)] string Prompt,
    [property: Id(4)] IReadOnlyList<V2GroundingDescriptor> Groundings);

public static class V2AssistantTools
{
    public const string Clarify = "assistant.clarify";
}

public static class V2CrossProviderTools
{
    public const string MatchSalesforceAccountToGmailSender = "cross.gmail-sender.salesforce-account.match";
}
