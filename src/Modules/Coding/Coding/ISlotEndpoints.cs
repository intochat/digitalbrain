namespace DigitalBrain.Coding;

// The HTTP a promotion needs: the standby's health, one read-only smoke read against it, and the
// gateway's switch. A seam, so a slot fact can promote with no kernel and no gateway running.
public interface ISlotEndpoints
{
    Task<bool> HealthyAsync(string slotUrl, CancellationToken cancellationToken = default);

    Task<string?> SmokeAsync(string slotUrl, string path, CancellationToken cancellationToken = default);

    // A silo learns of a lease flip through its own refresher, so the promotion asks the standby whether
    // the flip has reached it before it moves any traffic there.
    Task<bool> HoldsLeaseAsync(string slotUrl, string slot, CancellationToken cancellationToken = default);

    // Null when the switch was accepted; otherwise how long the gateway asks the caller to wait, because it
    // rebuilds its whole route table per update and debounces.
    Task<TimeSpan?> SwitchAsync(string gatewayUrl, string slot, CancellationToken cancellationToken = default);

    Task<string?> ActiveAsync(string gatewayUrl, CancellationToken cancellationToken = default);
}
