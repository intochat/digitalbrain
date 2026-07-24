namespace DigitalBrain.Kernel;

[Alias("db.internal.clock-turn-barrier")]
internal partial interface IClockTurnBarrier : IGrainWithStringKey
{
    [Alias(nameof(Barrier))]
    Task Barrier();
}
