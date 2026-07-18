using Ino.Core;

namespace Ino.Testing.Fixtures.DeltaContracts;

[GenerateSerializer]
public sealed record SomethingObserved([property: Id(0)] string What) : ISynapse;
