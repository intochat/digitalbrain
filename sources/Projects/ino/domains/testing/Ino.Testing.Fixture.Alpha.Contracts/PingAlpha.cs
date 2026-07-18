using Ino.Core;

namespace Ino.Testing.Fixtures.AlphaContracts;

[GenerateSerializer]
public sealed record PingAlpha([property: Id(0)] string Message) : ISynapse;

[GenerateSerializer]
public sealed record PingAlphaResponse([property: Id(0)] string AggregatedMessage) : ISynapse;
