using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetUsageEventsResponse
{
    [JsonPropertyName("summary")]
    [DataMember(Name = "summary")]
    public UsageSummaryResponse Summary { get; set; } = new();

    [JsonPropertyName("timeline")]
    [DataMember(Name = "timeline")]
    public List<UsageTimelinePointResponse> Timeline { get; set; } = [];

    [JsonPropertyName("events")]
    [DataMember(Name = "events")]
    public List<UsageEventItemResponse> Events { get; set; } = [];

    [JsonPropertyName("pagination")]
    [DataMember(Name = "pagination")]
    public UsagePaginationResponse Pagination { get; set; } = new();
}

public sealed class UsageSummaryResponse
{
    [JsonPropertyName("currentUsage")]
    [DataMember(Name = "currentUsage")]
    public decimal CurrentUsage { get; set; }

    [JsonPropertyName("monthlyLimit")]
    [DataMember(Name = "monthlyLimit")]
    public decimal MonthlyLimit { get; set; }

    [JsonPropertyName("remainingTokens")]
    [DataMember(Name = "remainingTokens")]
    public decimal RemainingTokens { get; set; }
}

public sealed class UsageTimelinePointResponse
{
    [JsonPropertyName("date")]
    [DataMember(Name = "date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("tokensConsumed")]
    [DataMember(Name = "tokensConsumed")]
    public decimal TokensConsumed { get; set; }

    [JsonPropertyName("eventsCount")]
    [DataMember(Name = "eventsCount")]
    public int EventsCount { get; set; }
}

public sealed class UsageEventItemResponse
{
    [JsonPropertyName("uniqueId")]
    [DataMember(Name = "uniqueId")]
    public Guid UniqueId { get; set; }

    [JsonPropertyName("occurredAt")]
    [DataMember(Name = "occurredAt")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("serviceType")]
    [DataMember(Name = "serviceType")]
    public string ServiceType { get; set; } = null!;

    [JsonPropertyName("source")]
    [DataMember(Name = "source")]
    public string Source { get; set; } = null!;

    [JsonPropertyName("tokensConsumed")]
    [DataMember(Name = "tokensConsumed")]
    public decimal TokensConsumed { get; set; }

    [JsonPropertyName("tripVault")]
    [DataMember(Name = "tripVault")]
    public UsageTripVaultResponse? TripVault { get; set; }
}

public sealed class UsageTripVaultResponse
{
    [JsonPropertyName("uniqueId")]
    [DataMember(Name = "uniqueId")]
    public Guid UniqueId { get; set; }

    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    public string Name { get; set; } = null!;
}

public sealed class UsagePaginationResponse
{
    [JsonPropertyName("page")]
    [DataMember(Name = "page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    [DataMember(Name = "pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalCount")]
    [DataMember(Name = "totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("totalPages")]
    [DataMember(Name = "totalPages")]
    public int TotalPages { get; set; }
}
