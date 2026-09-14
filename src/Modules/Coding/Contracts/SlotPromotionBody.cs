namespace DigitalBrain.Coding;

// FromSlot is the slot that is live when the promotion starts: the one whose traffic drains and whose
// resource is stopped for a forward-only landing. Attempt counts the health probes.
[GenerateSerializer]
[Alias("coding.slot-promotion-body")]
public sealed record SlotPromotionBody(
    [property: Id(0)] string FromSlot,
    [property: Id(1)] int Attempt);
