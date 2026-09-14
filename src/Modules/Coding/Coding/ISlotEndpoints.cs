namespace DigitalBrain.Coding;

// The HTTP a promotion needs: the standby's health, one read-only smoke read against it, and the
// gateway's switch. A seam, so a slot fact can promote with no kernel and no gateway running. The
// addresses arrive as Uris because SlotOptions already refused every unparsable one.
public interface ISlotEndpoints
{
    Task<bool> HealthyAsync(Uri slotUrl, CancellationToken cancellationToken = default);

    Task<string?> SmokeAsync(Uri slotUrl, string path, CancellationToken cancellationToken = default);

    // A silo learns of a lease flip through its own refresher, so the promotion asks the standby whether
    // the flip has reached it before it moves any traffic there. Only that silo's own answer counts: the
    // read is true when the silo names itself the asked slot and says it holds the lease.
    Task<bool> HoldsLeaseAsync(Uri slotUrl, string slot, CancellationToken cancellationToken = default);

    // The slot the silo at this address says it is, from the same read, and null when it did not answer
    // that read at all. A standby configured for a different slot never reports the lease for the one
    // being promoted, so the promotion reads the name before it flips anything: that way a typo in
    // 'DigitalBrain:Slot' is a configuration failure naming both slots, not an unexplained settle timeout.
    Task<string?> NamedSlotAsync(Uri slotUrl, string slot, CancellationToken cancellationToken = default);

    // Null when the switch was accepted; otherwise how long the gateway asks the caller to wait, because it
    // rebuilds its whole route table per update and debounces.
    Task<TimeSpan?> SwitchAsync(Uri gatewayUrl, string slot, CancellationToken cancellationToken = default);

    Task<string?> ActiveAsync(Uri gatewayUrl, CancellationToken cancellationToken = default);
}
