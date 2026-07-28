using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Services;

namespace DigitalBrain.Testing;

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
        GrainId id, Silo silo, ILoggerFactory loggerFactory, IGrainFactory grains)
        : base(id, silo, loggerFactory)
    {
        _grains = grains;

        if (!string.Equals(id.Type.ToString(), SourceType, StringComparison.Ordinal))
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
            .ReceiveReminder(reminderName, new TickStatus(firstTickTime, period, currentTickTime));
}
