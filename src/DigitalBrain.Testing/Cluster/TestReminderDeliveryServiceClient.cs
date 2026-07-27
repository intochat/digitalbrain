using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using Orleans.Runtime.Services;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Registered as a singleton grain-service client in the fixture silo.")]
internal sealed class TestReminderDeliveryServiceClient(
    IServiceProvider services) :
    GrainServiceClient<ITestReminderDeliveryService>(services),
    ITestReminderDeliveryServiceClient
{
    public Task Deliver(
        NeuronId target,
        string reminderName,
        DateTime firstTickTime,
        TimeSpan period,
        DateTime currentTickTime)
        => GetGrainService(CurrentGrainReference.GrainId)
            .Deliver(
                target,
                reminderName,
                firstTickTime,
                period,
                currentTickTime);
}
