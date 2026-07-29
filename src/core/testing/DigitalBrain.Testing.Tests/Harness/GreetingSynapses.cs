using DigitalBrain.Abstractions;

namespace DigitalBrain.TestingTests.Harness;

[GenerateSerializer]
[Alias("harness.say-hello")]
public sealed record SayHello([property: Id(0)] string Name) : Synapse;

[GenerateSerializer]
[Alias("harness.greeted")]
public sealed record Greeted([property: Id(0)] string Message) : Synapse;
