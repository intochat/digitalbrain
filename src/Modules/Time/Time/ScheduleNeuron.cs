using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Time;

[GrainType(ISchedule.GrainTypeName)]
public sealed class ScheduleNeuron : Neuron, ISchedule, IRemindable
{
    private const string StateName = "time.schedule";
    private const string ReminderPrefix = "time.schedule.";
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<ScheduleState> _states;

    public ScheduleNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<ScheduleState>>();
    }

    public Task<ScheduleSnapshot> Read()
    {
        var data = LoadRecorded();
        if (data is null)
        {
            return Task.FromResult(new ScheduleSnapshot(
                ScheduleStatus.Idle, 0, null, null, null, null, null, 0, null));
        }

        PrincipalId? principal = data.OnBehalfOfPrincipal is { } guid
            ? new PrincipalId(guid)
            : null;

        return Task.FromResult(new ScheduleSnapshot(
            data.Status,
            data.Generation,
            data.Period,
            data.NextDue,
            data.LastTickAt,
            data.Note,
            principal,
            data.LastCollapsedPeriods,
            data.LastResolution));
    }

    public async Task HandleAsync(ArmSchedule synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (synapse.PeriodSeconds <= 0)
        {
            throw new NeuronAuthorizationException(
                $"Schedule '{Id}' refuses a non-positive period.");
        }

        if (string.IsNullOrWhiteSpace(synapse.Note))
        {
            throw new NeuronAuthorizationException(
                $"Schedule '{Id}' refuses to arm without a note (tick payload).");
        }

        var current = LoadRecorded();
        if (current is { Status: ScheduleStatus.Armed })
        {
            throw new NeuronAuthorizationException(
                $"Schedule '{Id}' is already armed; cancel it before re-arming.");
        }

        // Host mints via VerifiedActor.Enter; ArmSchedule.OnBehalfOf is untrusted.
        // Ambient verified principal wins; a mismatched claim is spoof and refused.
        var actor = RequireVerifiedActor("arm");
        if (synapse.OnBehalfOf is { } claimed
            && claimed.PrincipalId != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException(
                $"Schedule '{Id}' refuses OnBehalfOf that does not match the verified principal.");
        }

        var generation = (current?.Generation ?? 0) + 1;
        var period = TimeSpan.FromSeconds(synapse.PeriodSeconds);
        var now = TimeProvider.GetUtcNow();
        var nextDue = now + period;
        var reminderName = ReminderName(generation);

        Stage(new ScheduleState(
            ScheduleStatus.Armed,
            generation,
            period,
            nextDue,
            LastTickAt: null,
            synapse.Note.Trim(),
            actor.PrincipalId.Value,
            actor.Username,
            reminderName,
            LastCollapsedPeriods: 0,
            LastResolution: null));

        await RegisterReminderAsync(reminderName, period)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var armed = new ScheduleArmed(
            synapse.CommandId, Id, generation, period, nextDue, synapse.Note.Trim(), actor);
        await ReplyAsync(armed, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await EmitAsync(armed)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(CancelSchedule synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var current = LoadRecorded();
        if (current is not { Status: ScheduleStatus.Armed })
        {
            throw new NeuronAuthorizationException(
                $"Schedule '{Id}' has no armed cadence to cancel.");
        }

        Stage(current with
        {
            Status = ScheduleStatus.Cancelled,
            ActiveReminderName = null,
        });

        await ReplyAsync(
            new ScheduleCancelled(synapse.CommandId, Id, current.Generation),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await EmitAsync(new ScheduleCancelled(synapse.CommandId, Id, current.Generation))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await RetireReminderAsync(current.ActiveReminderName)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ForceScheduleCatchUp synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var missed = synapse.MissedPeriods <= 0 ? 4 : synapse.MissedPeriods;
        var current = LoadRecorded();
        if (current is not { Status: ScheduleStatus.Armed })
        {
            throw new NeuronAuthorizationException(
                $"Schedule '{Id}' must be armed before force catch-up.");
        }

        // Backdate NextDue so catch-up collapses exactly `missed` periods.
        var now = TimeProvider.GetUtcNow();
        var syntheticDue = now - TimeSpan.FromTicks(current.Period.Ticks * (missed - 1));
        Stage(current with { NextDue = syntheticDue });

        var tick = await RunCatchUpAsync(current.Generation, current.ActiveReminderName ?? ReminderName(current.Generation))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)
            ?? throw new NeuronAuthorizationException(
                $"Schedule '{Id}' force catch-up produced no tick.");

        await ReplyAsync(tick, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!TryParseReminderName(reminderName, out var generation))
        {
            throw new InvalidOperationException(
                $"Schedule neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        await RunCatchUpAsync(generation, reminderName)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // Phase-preserving catch-up: one tick collapses N missed periods (Wave 5 gate).
    private async Task<ScheduleTick?> RunCatchUpAsync(long generation, string reminderName)
    {
        var data = LoadRecorded();
        if (data is null
            || data.Status != ScheduleStatus.Armed
            || data.Generation != generation
            || !string.Equals(data.ActiveReminderName, reminderName, StringComparison.Ordinal))
        {
            await RetireReminderAsync(reminderName)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return null;
        }

        var observedAt = TimeProvider.GetUtcNow();
        if (observedAt < data.NextDue)
        {
            await RegisterReminderAsync(reminderName, data.NextDue - observedAt)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return null;
        }

        var periodTicks = data.Period.Ticks;
        if (periodTicks <= 0)
        {
            await RetireReminderAsync(reminderName)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return null;
        }

        var dueAt = data.NextDue;
        var (collapsed, nextDue, resolution) = ScheduleCatchUp.Compute(
            dueAt,
            observedAt,
            data.Period,
            ReminderPeriod);

        ActorContext? onBehalf = data.OnBehalfOfPrincipal is { } guid
            ? new ActorContext(new PrincipalId(guid), data.OnBehalfOfUsername ?? "_schedule")
            : null;

        var rollback = SerializedState();
        Stage(data with
        {
            NextDue = nextDue,
            LastTickAt = observedAt,
            LastCollapsedPeriods = collapsed,
            LastResolution = resolution,
        });

        var tick = new ScheduleTick(
            Id,
            generation,
            dueAt,
            observedAt,
            nextDue,
            resolution,
            collapsed,
            data.Note,
            onBehalf);

        try
        {
            // Due → tick two-step (both journaled).
            await EmitAsync(new ScheduleDue(
                Id, generation, dueAt, observedAt, data.Note, onBehalf))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            using (VerifiedActor.Enter(onBehalf))
            {
                await EmitAsync(tick)
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

                // Corpus projection into the on-behalf principal partition (A18).
                if (onBehalf is null)
                {
                    throw new NeuronAuthorizationException(
                        $"Schedule '{Id}' refuses corpus projection without an on-behalf principal.");
                }

                await SendAsync(
                    ICorpus.ForPrincipal(Id.Owner, onBehalf.PrincipalId),
                    new AppendCorpusEntry(
                        CommandId.New(),
                        Kind: "time.schedule-tick",
                        Text: $"schedule {Id.Name} tick Resolution={resolution} CollapsedPeriods={collapsed} Note={data.Note}",
                        Correlation: null,
                        At: observedAt))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }
        catch
        {
            RestoreState(rollback);
            DeactivateOnIdle();
            throw;
        }

        var delay = nextDue - TimeProvider.GetUtcNow();
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        await RegisterReminderAsync(reminderName, delay)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return tick;
    }

    private ScheduleState? LoadRecorded()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(ScheduleState data) => _state.Value = _states.SerializeToArray(data);

    private byte[] SerializedState()
        => _state.Value is { } serialized ? serialized.ToArray() : [];

    private void RestoreState(byte[] serialized) => _state.Value = serialized;

    // VerifiedActor.Current is the only trusted Actor at arm time.
    // Payload OnBehalfOf may match it or be null; never overrides ambient.
    private static ActorContext RequireVerifiedActor(string command)
    {
        var verified = VerifiedActor.Current
            ?? throw new NeuronAuthorizationException(
                $"Schedule refuses '{command}' without a verified principal.");

        if (string.IsNullOrWhiteSpace(verified.Username))
        {
            throw new NeuronAuthorizationException(
                $"Schedule refuses '{command}' with an empty actor username.");
        }

        return verified;
    }

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A schedule command requires a command id.");
        }
    }

    private Task<Orleans.Runtime.IGrainReminder> RegisterReminderAsync(string reminderName, TimeSpan dueTime)
        => this.RegisterOrUpdateReminder(
            reminderName,
            dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime,
            ReminderPeriod);

    private async Task RetireReminderAsync(string? reminderName)
    {
        if (reminderName is not null
            && await this.GetReminder(reminderName)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } reminder)
        {
            await this.UnregisterReminder(reminder)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private static string ReminderName(long generation)
        => string.Create(CultureInfo.InvariantCulture, $"{ReminderPrefix}{generation}");

    private static bool TryParseReminderName(string reminderName, out long generation)
    {
        generation = 0;
        return reminderName.StartsWith(ReminderPrefix, StringComparison.Ordinal)
            && long.TryParse(
                reminderName.AsSpan(ReminderPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out generation)
            && generation > 0;
    }
}

