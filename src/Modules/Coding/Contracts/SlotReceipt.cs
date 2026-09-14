namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-receipt")]
public sealed record SlotReceipt(
    [property: Id(0)] string Slot,
    [property: Id(1)] SlotPhase Phase,
    [property: Id(2)] int Revision);
