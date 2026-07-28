using DigitalBrain.Abstractions;

namespace DigitalBrain.Quickstart;

[GenerateSerializer]
[Alias("quickstart.say-hello")]
public sealed record SayHello([property: Id(0)] string Name) : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
public sealed record Greeted([property: Id(0)] string Message) : Synapse;
