namespace DigitalBrain.Abstractions.Brain;

// An attention frame — NOT an auth boundary, NOT tenancy, NOT a partition, NOT a chat. It
// spans chats, MCP calls, and timers; per-context tallies bias resolution among its members.
[GenerateSerializer]
[Alias("db.brain-context")]
public sealed record BrainContext(
    [property: Id(0)] string Name,
    [property: Id(1)] IReadOnlyList<BrainReference> Members,
    [property: Id(2)] DateTimeOffset LastUsed,
    [property: Id(3)] IReadOnlyDictionary<string, int> Tallies);
