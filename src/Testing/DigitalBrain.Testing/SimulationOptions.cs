using DigitalBrain.Core;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Testing;

public sealed record SimulationOptions
{
    public IReadOnlyList<IModule> Modules { get; init; } = [];
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public Action<IClientBuilder>? ConfigureClient { get; init; }
    public IReadOnlyDictionary<string, string?>? Configuration { get; init; }
    public string? PersistenceDirectory { get; init; }
    public StorageFaults? StorageFaults { get; init; }
    public bool UseReminders { get; init; }
    public bool UseHttp { get; init; }
}
