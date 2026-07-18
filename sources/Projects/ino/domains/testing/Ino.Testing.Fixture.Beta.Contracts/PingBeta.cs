using Ino.Core;

namespace Ino.Testing.Fixtures.BetaContracts;

[GenerateSerializer]
public sealed record PingBeta([property: Id(0)] string Message) : ISynapse;

[GenerateSerializer]
public sealed record PingResponse([property: Id(0)] string Text) : ISynapse;
