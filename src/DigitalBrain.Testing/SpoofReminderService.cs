using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Runtime.Services;
using Orleans.Services;

namespace DigitalBrain.Testing;

internal interface ISpoofReminderService : IGrainService
{
    Task DeliverAsync(NeuronId target, string reminderName);
}

internal sealed class SpoofReminderService : GrainService, ISpoofReminderService
{
    private readonly IGrainFactory _grains;

    public SpoofReminderService(
        IServiceProvider services,
        GrainId id,
        Silo silo,
        ILoggerFactory loggerFactory,
        IGrainFactory grains)
        : base(id, silo, loggerFactory)
    {
        _grains = grains;
    }

    public Task DeliverAsync(NeuronId target, string reminderName)
        => _grains
            .GetGrain<IRemindable>(target.ToGrainId())
            .ReceiveReminder(reminderName, default);
}

internal interface ISpoofReminderServiceClient :
    IGrainServiceClient<ISpoofReminderService>,
    ISpoofReminderService;

internal sealed class SpoofReminderServiceClient(IServiceProvider services) :
    GrainServiceClient<ISpoofReminderService>(services),
    ISpoofReminderServiceClient
{
    public Task DeliverAsync(NeuronId target, string reminderName)
        => GetGrainService(CurrentGrainReference.GrainId)
            .DeliverAsync(target, reminderName);
}

[Alias("db.test.spoof-reminder-service-caller")]
[ClientEntryPoint]
internal partial interface ISpoofReminderServiceCaller : INeuron
{
    [Alias("Deliver")]
    Task DeliverAsync(NeuronId target, string reminderName);
}

internal sealed class SpoofReminderServiceCaller : Neuron, ISpoofReminderServiceCaller
{
    private readonly ISpoofReminderServiceClient _service;

    public SpoofReminderServiceCaller()
    {
        _service = ServiceProvider.GetRequiredService<ISpoofReminderServiceClient>();
    }

    public Task DeliverAsync(NeuronId target, string reminderName)
        => _service.DeliverAsync(target, reminderName);
}
