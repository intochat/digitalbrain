namespace DigitalBrain.Abstractions;

// Marks a handler failure that is the delivery's answer rather than a fault to try again. The
// kernel retracts the turn either way; a settled failure additionally consumes the delivery, so
// the outbox stops redelivering it and the fact stays journaled as received. Every other failure
// is transient: the turn's cause is retracted with it and the outbox redelivers.
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class SettledDeliveryFailureAttribute : Attribute;
