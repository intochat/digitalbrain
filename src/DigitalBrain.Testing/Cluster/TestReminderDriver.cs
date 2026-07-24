using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

internal sealed class TestReminderDriver
{
    private readonly ITestReminderDeliveryCaller _caller;
    private readonly List<DeliveredReminder> _diagnostics = [];
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
        _diagnostics.Add(new(
            target,
            due.ReminderName,
            due.Due,
            due.Period,
            due.ETag));
        return true;
    }

    internal string DescribePendingAtOrBefore(DateTimeOffset target)
        => _table.DescribePendingAtOrBefore(target, _scope);

    internal async Task BarrierAsync(
        GrainId registrationOwner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = NeuronId.FromGrainKey(
            registrationOwner.Type.ToString()
                ?? throw new InvalidOperationException(
                    "A timer registration owner has no grain type."),
            registrationOwner.Key.ToString());

        await _caller.Barrier(target);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed record DeliveredReminder(
        NeuronId Target,
        string Name,
        DateTimeOffset Due,
        TimeSpan Period,
        string ETag);
}
