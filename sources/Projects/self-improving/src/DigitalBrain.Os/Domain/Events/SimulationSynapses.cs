using DigitalBrain.Protocol.Domain.Events;

namespace DigitalBrain.Os.Domain.Events;

[GenerateSerializer]
public enum SimulationMode { Headless, Ui }

[GenerateSerializer]
public sealed record RunSimulation(
    [property: Id(0)] string Filter,
    [property: Id(1)] SimulationMode Mode) : Synapse;

[GenerateSerializer]
public sealed record SimulationScenarioResult(
    [property: Id(0)] string Name,
    [property: Id(1)] string Source,
    [property: Id(2)] string Outcome,
    [property: Id(3)] string Diagnostic,
    [property: Id(4)] string RenderedSurface) : Synapse;

[GenerateSerializer]
public sealed record SimulationReport(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Filter,
    [property: Id(2)] SimulationScenarioResult[] Results,
    [property: Id(3)] int Passed,
    [property: Id(4)] int Failed,
    [property: Id(5)] int Skipped,
    [property: Id(6)] string ArtifactPath) : Synapse;