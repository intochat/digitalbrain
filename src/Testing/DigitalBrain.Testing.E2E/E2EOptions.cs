using DigitalBrain.Core;

namespace DigitalBrain.Testing.E2E;

public sealed record E2EOptions
{
    public required IApplicationConfiguration Application { get; init; }
    public TestExecutionOptions Execution { get; init; } = new();
    public BrowserOptions Browser { get; init; } = new();
}
