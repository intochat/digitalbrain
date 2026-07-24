using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[Alias("time.countdown-wakeup")]
[ClientEntryPoint]
internal partial interface ICountdownWakeup : IGrainWithStringKey
{
    [Alias(nameof(Wake))]
    Task Wake(long generation, long revision);
}
