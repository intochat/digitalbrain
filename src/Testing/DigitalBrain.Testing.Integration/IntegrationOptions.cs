using DigitalBrain.Core;

namespace DigitalBrain.Testing.Integration;

public sealed record IntegrationOptions
{
    public IReadOnlyList<ModuleDefinition> Modules { get; init; } = [];
    public TestExecutionOptions Execution { get; init; } = new();
}
