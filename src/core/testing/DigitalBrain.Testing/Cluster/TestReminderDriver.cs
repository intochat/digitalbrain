using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

internal sealed class TestReminderDriver
{
    private readonly ITestReminderDeliveryCaller _caller;
    private readonly string _scope;
    private readonly VolatileReminderTable _table;

    internal TestReminderDriver(
        VolatileReminderTable table,
        IGrainFactory grains,
        string scope)
    {
        _table = table;
        _scope = scope;

        var caller = NeuronId.For<ITestReminderDeliveryCaller>(
            new OwnerId($"{scope}-reminder-driver"),
            "caller");
        _caller = grains.GetGrain<ITestReminderDeliveryCaller>(
            caller.ToGrainId());
    }

    internal DateTimeOffset? NextDueAtOrBefore(DateTimeOffset target)
        => _table.NextDueAtOrBefore(target, _scope)?.Due;

    internal async Task<bool> TryDeliverNextDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var due = _table.NextDueAtOrBefore(now, _scope);
        if (due is null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var target = NeuronId.FromGrainKey(
            due.GrainId.Type.ToString()
                ?? throw new InvalidOperationException(
                    "A due reminder has no grain type."),
            due.GrainId.Key.ToString());

        await _caller.Deliver(
            target,
            due.ReminderName,
            due.FirstTickTime.UtcDateTime,
            due.Period,
            due.Due.UtcDateTime);

        _table.CompleteDelivery(due);
        return true;
    }
}
