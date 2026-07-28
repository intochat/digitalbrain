using DigitalBrain.Abstractions;
using Orleans.Services;

namespace DigitalBrain.Testing;

internal interface ITestReminderDeliveryService : IGrainService
{
    Task Deliver(
        NeuronId target,
        string reminderName,
        DateTime firstTickTime,
        TimeSpan period,
        DateTime currentTickTime);
}
