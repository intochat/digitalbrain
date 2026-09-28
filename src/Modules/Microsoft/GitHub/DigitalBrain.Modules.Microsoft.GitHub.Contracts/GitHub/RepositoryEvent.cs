namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("github.repository-event")]
public sealed record RepositoryEvent(
    [property: Id(0)] string DeliveryId,
    [property: Id(1)] string Event,
    [property: Id(2)] string? Action,
    [property: Id(3)] int? Number,
    [property: Id(4)] string BodyHash);