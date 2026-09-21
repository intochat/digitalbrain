using DigitalBrain.Contracts;
namespace DigitalBrain.Flutter.Collection;

[GenerateSerializer, Alias("ui.collection-item")]
public sealed record CollectionItem(
    [property: Id(0)] string Id, [property: Id(1)] string Label,
    [property: Id(2)] string Kind, [property: Id(3)] string? SecondaryText = null,
    [property: Id(4)] long? SizeBytes = null, [property: Id(5)] DateTimeOffset? ModifiedAt = null,
    [property: Id(6)] bool CanActivate = true);

[GenerateSerializer, Alias("ui.collection-activated")]
public sealed record CollectionActivated([property: Id(0)] string Name, [property: Id(1)] string ItemId, [property: Id(2)] long Revision) : Signal;
