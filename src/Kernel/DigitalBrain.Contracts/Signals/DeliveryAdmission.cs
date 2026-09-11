namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.delivery-admission")]
public enum DeliveryAdmission
{
    Accepted,
    Duplicate,
    Busy,
}
