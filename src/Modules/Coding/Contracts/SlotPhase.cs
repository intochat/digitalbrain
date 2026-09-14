namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-phase")]
public enum SlotPhase
{
    Idle = 0,
    Building = 1,
    Built = 2,
    Starting = 3,
    Smoking = 4,
    Live = 5,
    // Reserved and never assigned in phase 2: the retiring slot drains inside the promoting reaction's
    // grace wait, so no snapshot is saved for that moment. A phase that drains on its own would use it.
    Draining = 6,
    Retired = 7,
    Failed = 8,
}
