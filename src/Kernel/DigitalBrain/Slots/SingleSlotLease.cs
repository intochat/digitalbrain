namespace DigitalBrain.Core;

// One silo, no slots: it always holds the lease, so the fence is invisible to phase 0 and phase 1 hosts.
public sealed class SingleSlotLease(string slot = "") : IActiveSlotLease
{
    public string Slot { get; } = slot;

    public bool HoldsLease => true;

    public long Generation => 0;

    // There is no row to hand over, so only this slot's own name can be acquired.
    public Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(slot, Slot, StringComparison.OrdinalIgnoreCase));

    public Task ReleaseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
