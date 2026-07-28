using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans neuron activated by grain identity, not by the fixture.")]
internal sealed class TestReminderDeliveryCaller :
    Neuron,
    ITestReminderDeliveryCaller
{
    private readonly ITestReminderDeliveryServiceClient _service;

    public TestReminderDeliveryCaller()
    {
        _service = ServiceProvider.GetRequiredService<ITestReminderDeliveryServiceClient>();
    }

    public Task Deliver(
        NeuronId target,
        string reminderName,
        DateTime firstTickTime,
        TimeSpan period,
        DateTime currentTickTime)
        => _service.Deliver(target, reminderName, firstTickTime, period, currentTickTime);
}
