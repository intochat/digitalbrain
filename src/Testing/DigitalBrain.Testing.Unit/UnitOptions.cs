using DigitalBrain.Core;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Testing.Unit;

public sealed record UnitOptions
{
    public IReadOnlyList<IModule> Modules { get; init; } = [];
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public Action<IClientBuilder>? ConfigureClient { get; init; }
    public bool UseReminders { get; init; }
}
