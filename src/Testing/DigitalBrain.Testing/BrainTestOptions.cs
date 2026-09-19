using Orleans;
namespace DigitalBrain.Testing;
public sealed record BrainTestOptions
{
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public Action<IClientBuilder>? ConfigureClient { get; init; }
    public string? PersistenceDirectory { get; init; }
    public StorageFaults? StorageFaults { get; init; }
    public bool UseReminders { get; init; }
}
