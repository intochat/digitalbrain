using DigitalBrain.Core;

namespace DigitalBrain.Testing.Integration;

internal sealed record IntegrationOptions
{
    public IReadOnlyList<ModuleDefinition> Modules { get; init; } = [];
    public TestExecutionOptions Execution { get; init; } = new();
}
