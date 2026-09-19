namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.signal-admission")]
public enum SignalAdmission
{
    Accepted,
    Duplicate,
    Busy,
}
