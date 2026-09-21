using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Collection;

[Alias("collection"), Orleans.Metadata.DefaultGrainType(UIVocabulary.CollectionType)]
public interface ICollectionView : INeuron
{
    Task Set(CollectionDefinition definition, long expectedRevision);
    Task Select(string itemId, long expectedRevision);
    Task Activate(string itemId, long expectedRevision);
    [ReadOnly] Task<CollectionState> Read();
}

[GenerateSerializer, Alias("ui.collection-definition")]
public sealed record CollectionDefinition([property: Id(0)] IReadOnlyList<CollectionItem> Items, [property: Id(1)] string? Selection = null, [property: Id(2)] string? Cursor = null, [property: Id(3)] string? Error = null);

[GenerateSerializer, Alias("ui.collection-state")]
public sealed class CollectionState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public CollectionDefinition Definition { get; set; } = new([]);
}

[GenerateSerializer, Alias("ui.collection-changed")]
public sealed record CollectionChanged([property: Id(0)] string Name, [property: Id(1)] long Revision) : Signal;
