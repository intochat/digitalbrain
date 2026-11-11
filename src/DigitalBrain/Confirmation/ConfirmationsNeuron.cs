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
    private const string ExpiryTimer = "confirmation-expiry";

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
        Schedule();
    }

    public async Task<ConfirmationResult?> Request(ConfirmationPlan plan)
    {
        await Expire().ConfigureAwait(true);
        var caller = Context.Caller;
        var raisedBy = caller?.Via?.ToString() ?? caller?.Actor.ToString() ?? "edge";
        var requester = Principal(caller);

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
        await Expire().ConfigureAwait(true);
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
        var caller = RequireCaller(required: false);
        var person = caller is null || caller is { Direct: true, Conversation: null, Actor: Actor.Person };
        var approver = caller is null ? ConfirmationPrincipal.Person("person") : Principal(caller);
        await Expire().ConfigureAwait(true);
        if (State.Answer(planId, approve, person, approver, Clock.GetUtcNow()) is { } answered)
        {
            await Commit(answered).ConfigureAwait(true);
        }
    }

    protected override Task OnReminder(string name, CancellationToken cancellationToken)
        => name == ExpiryTimer ? Expire() : Task.CompletedTask;

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
        Schedule();
    }

    private void Schedule()
    {
        var pending = State.Entries.Where(e => e.Result is null).ToArray();
        if (pending.Length == 0)
        {
            Context.Timers.Stop(ExpiryTimer);
            return;
        }
        var due = pending.Min(e => e.Request.ExpiresAt) - Clock.GetUtcNow();
        Context.Timers.Start(ExpiryTimer, due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1), null, keepAlive: true);
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

    private static ConfirmationPrincipal Principal(Caller? caller) => caller?.Actor switch
    {
        Actor.Person p => ConfirmationPrincipal.Person(p.Principal),
        Actor.App a => ConfirmationPrincipal.App(string.IsNullOrEmpty(a.Key) ? a.For.Principal : a.Key),
        Actor.Agent ag => ConfirmationPrincipal.Agent(ag.For.Principal),
        _ => ConfirmationPrincipal.Agent("unknown")
    };
}
