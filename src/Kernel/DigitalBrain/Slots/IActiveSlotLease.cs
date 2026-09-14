namespace DigitalBrain.Core;

// Which slot may react. HoldsLease is answered from a cached read that a hosted refresher renews, because
// every incoming grain call consults it; TryAcquireAsync is the compare-and-swap that hands it over.
public interface IActiveSlotLease
{
    string Slot { get; }

    bool HoldsLease { get; }

    long Generation { get; }

    Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default);

    Task ReleaseAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
