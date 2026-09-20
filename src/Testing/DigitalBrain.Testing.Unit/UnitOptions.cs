using DigitalBrain.Core;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Testing.Unit;

internal sealed record UnitOptions
{
    public IReadOnlyList<ModuleDefinition> Modules { get; init; } = [];
    public TestExecutionOptions Execution { get; init; } = new();
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public Action<IClientBuilder>? ConfigureClient { get; init; }
    public bool UseReminders { get; init; }
}
