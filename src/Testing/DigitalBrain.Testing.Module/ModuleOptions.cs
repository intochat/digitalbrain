using DigitalBrain.Kernel;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Testing.Module;

internal sealed record ModuleOptions
{
    public IReadOnlyList<ModuleDefinition> Modules { get; init; } = [];
    public TestExecutionOptions Execution { get; init; } = new();
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public Action<IClientBuilder>? ConfigureClient { get; init; }
    public bool UseReminders { get; init; }
    public bool HttpEdge { get; init; }
}
