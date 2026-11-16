using DigitalBrain.Access;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

namespace DigitalBrain.Confirmation;

/// <summary>
/// Core confirmations neuron. Hosts the confirmation state machine and inbox for a brain.
/// </summary>
[Neuron("runtime.confirmations")]
public sealed class ConfirmationsNeuron : Neuron<ConfirmationState>, IConfirmationRequests, IConfirmationInbox
{
    public const string NeuronType = "runtime.confirmations";
    private const string ExpiryReminder = "confirmation-expiry";
    private static readonly TimeSpan ExpiryPeriod = TimeSpan.FromMinutes(1);

    private TimeProvider Clock => Context.Time;

    private ConfirmationPolicy Policy =>
        (Seat.Services.GetService(typeof(ConfirmationPolicies)) as ConfirmationPolicies)?.For(Id.Brain.Value)
        ?? ConfirmationPolicy.DestructiveOnly;

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        if (Id.Key != IConfirmations.ConfirmationsNeuronKey)
        {
            throw new InvalidOperationException("A confirmation inbox has the key confirmations.");
        }
        await Expire().ConfigureAwait(true);
        await ScheduleAsync().ConfigureAwait(true);
    }

    public async Task<ConfirmationResult?> Request(ConfirmationPlan plan)
    {
        await Expire().ConfigureAwait(true);
        var caller = Context.Caller;
        var raisedBy = caller?.Via?.ToString() ?? caller?.Actor.ToString() ?? "edge";
        var requester = Answerer.PrincipalOf(caller);

        if (State.Request(plan, requester, raisedBy, Policy, Clock.GetUtcNow()) is not { } requested)
        {
            return State.Known(plan.Id);
        }

        await Commit(requested).ConfigureAwait(true);
        return requested.Result;
    }

    public async Task<ConfirmationResult?> Outcome(string planId)
    {
        await Expire().ConfigureAwait(true);
        return State.Known(planId);
    }

    public async Task Withdraw(string planId)
    {
        var caller = RequireCaller(required: false);
        var answerer = Answerer.Of(caller, Id.Brain);
        var raisedBy = caller?.Via?.ToString() ?? caller?.Actor.ToString() ?? "edge";
        await Expire().ConfigureAwait(true);
        if (State.Entries.FirstOrDefault(e => e.Request.PlanId == planId && e.Result is null) is { } open
            && !answerer.IsPerson
            && !string.Equals(open.RaisedBy, raisedBy, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only who raised this question withdraws it.");
        }
        if (State.Withdraw(planId, Clock.GetUtcNow()) is { } withdrawn)
        {
            await Commit(withdrawn).ConfigureAwait(true);
        }
    }

    public async Task<ConfirmationInbox> Read()
    {
        RequireCaller(required: false);
        await Expire().ConfigureAwait(true);
        return new(State.Revision, [.. State.Entries.Where(e => e.Result is null).Select(e => e.Request)]);
    }

    public async Task Resolve(string planId, bool approve)
    {
        var answerer = Answerer.Of(Context.Caller, Id.Brain);
        await Expire().ConfigureAwait(true);
        if (State.Answer(planId, approve, answerer, Clock.GetUtcNow()) is { } answered)
        {
            await Commit(answered).ConfigureAwait(true);
        }
    }

    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        if (name != ExpiryReminder) { return; }
        await Expire().ConfigureAwait(true);
        await ScheduleAsync().ConfigureAwait(true);
    }

    private async Task Expire()
    {
        var now = Clock.GetUtcNow();
        foreach (var entry in State.Entries.Where(e => e.Result is null && e.Request.ExpiresAt <= now).ToArray())
        {
            if (State.Expire(entry.Request.PlanId, now) is { } expired)
            {
                await Commit(expired).ConfigureAwait(true);
            }
        }
    }

    private async Task Commit(ConfirmationTransition transition)
    {
        await Save(transition.Next, transition.Signal).ConfigureAwait(true);
        await ScheduleAsync().ConfigureAwait(true);
    }

    private async Task ScheduleAsync()
    {
        var pending = State.Entries.Where(e => e.Result is null).ToArray();
        if (pending.Length == 0)
        {
            await Context.Reminders.Stop(ExpiryReminder).ConfigureAwait(true);
            return;
        }
        var due = pending.Min(e => e.Request.ExpiresAt) - Clock.GetUtcNow();
        await Context.Reminders.Start(
            ExpiryReminder,
            due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1),
            ExpiryPeriod).ConfigureAwait(true);
    }

    private Caller? RequireCaller(bool required)
    {
        var caller = Context.Caller;
        if (caller is null)
        {
            if (required) { throw new UnauthorizedAccessException("A trusted caller is required."); }
            return null;
        }
        if (caller.Brain != Id.Brain)
        {
            throw new UnauthorizedAccessException("This confirmation belongs to another brain.");
        }
        return caller;
    }
}
