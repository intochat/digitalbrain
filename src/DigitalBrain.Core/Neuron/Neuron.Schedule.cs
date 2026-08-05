using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain;

public abstract partial class Neuron
{
    private readonly Dictionary<string, ArmedScheduleTimer> scheduleTimers = new(StringComparer.Ordinal);

    private Task ReceiveScheduleAsync(Schedule schedule, DeliveryEnvelope envelope)
        => RunCoreReceptionAsync(schedule, envelope, (heardFrom, heardPosition, now) =>
        {
            if (ScheduleRefusalOf(schedule) is { } reason)
            {
                return StageCoreSaid(
                    new ScheduleFailed(NeuronId.KindOf(schedule.Fact.GetType()), reason, ConsecutiveFailures: 0),
                    heardFrom,
                    now,
                    directedTo: envelope.Source);
            }

            var factKind = catalog.KindOfFact(schedule.Fact.GetType());
            journal.SetSchedule(factKind, new ScheduleEntry(
                factKind,
                codec.Encode(schedule.Fact),
                schedule.Period,
                now + schedule.Period,
                ConsecutiveFailures: 0,
                Cause: heardPosition));
            return false;
        });

    private Task ReceiveUnscheduleAsync(Unschedule unschedule, DeliveryEnvelope envelope)
        => RunCoreReceptionAsync(unschedule, envelope, (_, _, _) =>
        {
            _ = journal.RemoveSchedule(unschedule.Fact);
            return false;
        });

    private string? ScheduleRefusalOf(Schedule schedule)
    {
        var scheduledType = schedule.Fact.GetType();
        var kind = NeuronId.KindOf(scheduledType);
        if (!catalog.TryGetFactType(kind, out var cataloged) || cataloged != scheduledType)
        {
            return $"'{kind}' is not a fact kind in the running catalog";
        }

        if (!catalog.ListenerKindsOf(scheduledType).Contains(Id.Kind))
        {
            return $"'{Id.Kind}' does not declare INeuron<{scheduledType.Name}>; a tick nobody handles is a dead claim";
        }

        return schedule.Period > TimeSpan.Zero ? null : "the schedule period must be positive";
    }

    private void SyncScheduleTimers()
    {
        var table = journal.ScheduleSnapshot();

        foreach (var stale in scheduleTimers.Keys.Except(table.Keys, StringComparer.Ordinal).ToArray())
        {
            scheduleTimers[stale].Timer.Dispose();
            scheduleTimers.Remove(stale);
        }

        var now = clock.GetUtcNow();
        foreach (var (factKind, entry) in table)
        {
            if (scheduleTimers.TryGetValue(factKind, out var armed))
            {
                if (armed.Period == entry.Period && armed.Cause == entry.Cause)
                {
                    continue;
                }

                armed.Timer.Dispose();
            }

            var due = entry.NextDue - now;
            if (due < TimeSpan.Zero)
            {
                due = TimeSpan.Zero;
            }

            var timer = this.RegisterGrainTimer(
                TickAsync,
                factKind,
                new GrainTimerCreationOptions { DueTime = due, Period = entry.Period });
            scheduleTimers[factKind] = new ArmedScheduleTimer(timer, entry.Period, entry.Cause);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Physics #4 against the timer-swallowing constraint: every tick failure is counted and, at the limit, journaled as ScheduleFailed — never silent, never lost inside a swallowing timer. Commit failures poison and deliberately escape.")]
    private async Task TickAsync(string factKind, CancellationToken cancellationToken)
    {
        if (poisoned)
        {
            return;
        }

        if (journal.ScheduleOf(factKind) is not { } entry)
        {
            SyncScheduleTimers();
            return;
        }

        string? failure = null;
        try
        {
            if (!catalog.TryGetFactType(factKind, out var factType))
            {
                throw new InvalidOperationException($"scheduled kind '{factKind}' is not in the running catalog");
            }

            var fact = codec.Decode(entry.Fact, factType) as Synapse
                ?? throw new InvalidOperationException($"the scheduled '{factKind}' body does not rehydrate");
            var envelope = new DeliveryEnvelope(
                Id,
                journal.LastSeq + 1,
                clock.GetUtcNow(),
                new SynapseRef(Id, entry.Cause),
                Answers: null);
            await DeliverToSelfAsync(fact, envelope, asQuestion: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (!poisoned)
        {
            failure = exception.Message;
        }

        var now = clock.GetUtcNow();
        var failures = failure is null ? 0 : entry.ConsecutiveFailures + 1;
        if (failure is null || failures < DeliveryPolicy.ScheduleFailureLimit)
        {
            journal.SetSchedule(factKind, entry with { NextDue = now + entry.Period, ConsecutiveFailures = failures });
            await CommitCoreBatchAsync(deliverable: false);
            return;
        }

        var deliverable = StageCoreSaid(
            new ScheduleFailed(factKind, failure, failures),
            new SynapseRefEntry(Id.Kind, Id.Name, entry.Cause),
            now);
        journal.RemoveSchedule(factKind);
        await CommitCoreBatchAsync(deliverable);
        SyncScheduleTimers();
    }

    private readonly record struct ArmedScheduleTimer(IGrainTimer Timer, TimeSpan Period, long Cause);
}
