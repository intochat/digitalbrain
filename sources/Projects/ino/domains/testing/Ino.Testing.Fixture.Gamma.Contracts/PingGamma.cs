using Ino.Core;

namespace Ino.Testing.Fixtures.GammaContracts;

[GenerateSerializer]
public sealed record PingGamma([property: Id(0)] string Message) : ISynapse;
