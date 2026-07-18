namespace Ino.Core.Hosting;

[GenerateSerializer]
public sealed record DiscoveryDump(
    [property: Id(0)] IReadOnlyList<CanonicalTarget> Canonical,
    [property: Id(1)] IReadOnlyList<ReactiveTarget> Reactive,
    [property: Id(2)] IReadOnlyDictionary<string, int> CountsBySilo);
