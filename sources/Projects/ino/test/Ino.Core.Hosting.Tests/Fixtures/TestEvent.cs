using Ino.Core;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Minimal ISynapse event used to exercise the Neuron&lt;TEvent&gt; base class
/// in integration tests.
/// </summary>
[GenerateSerializer]
public sealed record TestEvent(
    [property: Id(0)] string Text,
    [property: Id(1)] int Delta) : ISynapse;
