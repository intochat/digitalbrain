using Ino.Core;

namespace Ino.Core.Capabilities;

[GenerateSerializer]
public sealed record RoutingResult(
    [property: Id(0)] NeuronResult Outcome,
    [property: Id(1)] RoutingSource Source,
    [property: Id(2)] string? ScenarioName);

[GenerateSerializer]
public enum RoutingSource
{
    Unrouted = 0,
    Regex = 1,
    Ml = 2,
    Llm = 3,
}
