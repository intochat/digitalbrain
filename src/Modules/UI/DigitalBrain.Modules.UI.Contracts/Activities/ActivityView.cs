using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.activity-view")]
public sealed record ActivityView(
    [property: Id(0)] string Id,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] string RootSignalId,
    [property: Id(3)] string TriggerName,
    [property: Id(4)] string Title,
    [property: Id(5)] string Status,
    [property: Id(6)] DateTimeOffset StartedAt,
    [property: Id(7)] DateTimeOffset UpdatedAt,
    [property: Id(8)] IReadOnlyList<string> ParticipantNeuronIds,
    [property: Id(9)] IReadOnlyList<ActivityEventView> Events,
    [property: Id(10)] string? CommandId = null,
    [property: Id(11)] string? Detail = null,
    [property: Id(12)] long Version = 0);
