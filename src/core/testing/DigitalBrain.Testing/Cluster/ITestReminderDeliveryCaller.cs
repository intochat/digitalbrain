using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

[Alias("db.test.reminder-delivery-caller")]
[ClientEntryPoint]
internal partial interface ITestReminderDeliveryCaller : INeuron
{
    [Alias(nameof(Deliver))]
    Task Deliver(
        NeuronId target,
        string reminderName,
        DateTime firstTickTime,
        TimeSpan period,
        DateTime currentTickTime);
}
