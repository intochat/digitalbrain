using DigitalBrain.Abstractions;

namespace DigitalBrain.Tests.Harness;

internal static class TestActors
{
    // Deterministic operator stamp for tests and owner scripts that are not HTTP-authenticated.
    internal static ActorContext Operator { get; } = new(
        new PrincipalId(Guid.Parse("00000000-0000-0000-0000-0000000000a1")),
        "operator");
}
