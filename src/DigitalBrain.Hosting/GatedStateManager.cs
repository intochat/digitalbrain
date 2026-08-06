using System.Diagnostics.CodeAnalysis;
using Orleans.Journaling;

namespace DigitalBrain;

internal sealed class GatedStateManager(IJournaledStateManager inner)
    : IJournaledStateManager, ILifecycleParticipant<IGrainLifecycle>
{
    public long PendingWriteByteCount => inner.PendingWriteByteCount;

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
        => inner.InitializeAsync(cancellationToken);

    public void RegisterState(string name, IJournaledState state)
    {
        if (!Journal.CoreKeys.Contains(name))
        {
            throw new InvalidOperationException(
                $"Durable key '{name}' is not Core-owned; keyed IDurable* resolution is sealed away "
                + "from modules — all durable module state lives in TState (§5).");
        }

        inner.RegisterState(name, state);
    }

    public bool TryGetState(string name, [NotNullWhen(true)] out IJournaledState? state)
        => inner.TryGetState(name, out state);

    public ValueTask WriteStateAsync(CancellationToken cancellationToken)
        => inner.WriteStateAsync(cancellationToken);

    public ValueTask DeleteStateAsync(CancellationToken cancellationToken)
        => inner.DeleteStateAsync(cancellationToken);

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    public void Participate(IGrainLifecycle observer)
    {
        if (inner is ILifecycleParticipant<IGrainLifecycle> participant)
        {
            participant.Participate(observer);
        }
    }
}
