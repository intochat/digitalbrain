using DigitalBrain.Spi;
using DigitalBrain.Time;

namespace DigitalBrain;

/// <summary>An <see cref="IInstallHooks"/> that does nothing, for hosts that wire neither edges nor schedules.</summary>
public sealed class NoInstallHooks : IInstallHooks
{
    /// <inheritdoc />
    public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RemoveReactionEdges(NeuronId state, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RegisterSchedules(NeuronId state, IReadOnlyList<Schedule> schedules, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task UnregisterSchedules(NeuronId state, CancellationToken ct) => Task.CompletedTask;
}
