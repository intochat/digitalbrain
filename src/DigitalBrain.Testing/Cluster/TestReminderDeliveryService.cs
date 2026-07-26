using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Services;
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

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain service activated by the runtime.")]
internal sealed class TestReminderDeliveryService :
    GrainService,
    ITestReminderDeliveryService
{
    internal const string SourceType = "sys.svc.user.98B63662";

    private readonly IGrainFactory _grains;

    public TestReminderDeliveryService(
        GrainId id,
        Silo silo,
        ILoggerFactory loggerFactory,
        IGrainFactory grains)
        : base(id, silo, loggerFactory)
    {
        _grains = grains;

        if (!string.Equals(
            id.Type.ToString(),
            SourceType,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The test reminder delivery service source changed from expected '{SourceType}' to '{id.Type}'. Update the exact fixture allowlist deliberately.");
        }
    }

    public Task Deliver(
        NeuronId target,
        string reminderName,
        DateTime firstTickTime,
        TimeSpan period,
        DateTime currentTickTime)
        => _grains
            .GetGrain<IRemindable>(target.ToGrainId())
            .ReceiveReminder(
                reminderName,
                new TickStatus(firstTickTime, period, currentTickTime));

}

internal interface ITestReminderDeliveryServiceClient :
    IGrainServiceClient<ITestReminderDeliveryService>,
    ITestReminderDeliveryService;

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
        _service = ServiceProvider
            .GetRequiredService<ITestReminderDeliveryServiceClient>();
    }

    public Task Deliver(
        NeuronId target,
        string reminderName,
        DateTime firstTickTime,
        TimeSpan period,
        DateTime currentTickTime)
        => _service.Deliver(
            target,
            reminderName,
            firstTickTime,
            period,
            currentTickTime);

}
