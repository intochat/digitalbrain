using DigitalBrain.Contracts;

namespace DigitalBrain.Compute.Usage;

[GenerateSerializer, Alias("compute.usage-row")]
public sealed record UsageRow([property: Id(0)] string Id, [property: Id(1)] string Payload,
    [property: Id(2)] DateTimeOffset OccurredAt, [property: Id(3)] long? Revision = null);
[GenerateSerializer, Alias("compute.usage-page")]
public sealed record UsagePage([property: Id(0)] IReadOnlyList<UsageRow> Items, [property: Id(1)] string? NextCursor);

[Alias("compute.usage"), Orleans.Metadata.DefaultGrainType("compute.usage")]
public interface IComputeUsage : INeuron
{
    Task Append(string workspace, string id, string payload, DateTimeOffset revision, CancellationToken ct = default);
    Task<UsagePage> Read(string workspace, int limit, string? cursor, CancellationToken ct = default);
}
