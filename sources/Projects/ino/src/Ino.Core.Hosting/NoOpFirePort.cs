using Ino.Core;

namespace Ino.Core.Hosting;

/// <summary>
/// Fallback port used during test construction and DI boot sequencing
/// where a real port is not yet available. Returns NeuronResult.Ok() and
/// completes broadcasts silently.
/// </summary>
public sealed class NoOpFirePort : IFirePort
{
    public Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
        => Task.FromResult(NeuronResult.Ok());

    public Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
        => Task.CompletedTask;
}
