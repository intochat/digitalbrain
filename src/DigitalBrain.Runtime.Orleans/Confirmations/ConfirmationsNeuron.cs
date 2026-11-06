using DigitalBrain.Capabilities;
using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans.Confirmations;

/// <summary>
/// The confirmations neuron. The alias <c>confirmations</c> (<see cref="IConfirmationInbox"/>) addresses this type
/// <c>runtime.confirmations</c> at instance key <c>confirmations</c>.
/// </summary>
[DigitalBrain.Neuron("runtime.confirmations")]
public sealed class ConfirmationsNeuron(IOptions<ConfirmationOptions> options)
    : DigitalBrain.Neuron<ConfirmationState>, IConfirmationEntity, IConfirmationInbox
{
    private const string ExpiryTimer = "confirmation-expiry";
    private const int PendingLimit = 20;
    private const int MaxEntries = 50;

    private TimeProvider Clock => Context.Time;

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        if (Id.Key != IConfirmations.ConfirmationsNeuronKey) { throw new InvalidOperationException("A confirmation inbox has the key confirmations."); }
        await Expire();
        Schedule();
    }

    public async Task Begin(ConfirmationPlan plan, string origin)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        if (plan.Id.Length > 128 || plan.Expiry <= TimeSpan.Zero) { throw new ArgumentException("Invalid confirmation plan.", nameof(plan)); }
        RequireOwnCaller(required: false);
        if (State.Entries.Any(entry => entry.Request.PlanId == plan.Id))
        { throw new InvalidOperationException("This confirmation plan was already requested."); }
        if (State.Entries.Count(entry => entry.Result is null) >= PendingLimit)
        { throw new InvalidOperationException("This brain already has 20 confirmations waiting."); }
        var requester = Requester(origin);
        var policy = ConfirmationPolicyPreference.For(options.Value, Id.Brain.Value);
        var expiry = plan.Expiry < ConfirmationRules.DefaultExpiry ? plan.Expiry : ConfirmationRules.DefaultExpiry;
        var request = new ConfirmationRequested(plan.Id, plan with { Expiry = expiry }, requester, policy, Clock.GetUtcNow() + expiry);
        var asks = ConfirmationRules.Asks(policy, plan);
        var result = asks ? null : new ConfirmationResult(plan.Id, ConfirmationOutcome.Approved, ConfirmationDenyReason.None, true);
        var next = State with { Revision = State.Revision + 1, Entries = BoundEntries([.. State.Entries, new ConfirmationEntry(request, result)]) };
        await Save(next, asks ? request : ConfirmationAudit.Record(request, result!, null, Clock.GetUtcNow()));
        Schedule();
    }

    public async Task<ConfirmationInbox> Read()
    {
        RequireOwnCaller(required: true);
        await Expire();
        return new(State.Revision, [.. State.Entries.Where(entry => entry.Result is null).Select(entry => entry.Request)]);
    }

    public async Task<ConfirmationResult?> Result(string planId)
    {
        RequireOwnCaller(required: false);
        await Expire();
        return State.Entries.FirstOrDefault(entry => entry.Request.PlanId == planId)?.Result;
    }

    public async Task Resolve(string planId, bool approve)
    {
        var caller = RequireOwnCaller(required: true)!;
        var direct = string.IsNullOrEmpty(global::Orleans.Runtime.RequestContext.Get(HostNeuronContext.CallerKey) as string);
        var human = direct && caller.Kind == CallerKind.User && caller.StampedBy == TrustedEdge.AuthenticatedHttp
            && caller.AppId is null && caller.OnBehalfOf is null
            && !RuntimeCallerContext.IsModelOriginated(caller);
        var principal = human ? ConfirmationPrincipal.Person(caller.PrincipalId) : Approver(caller);
        await Expire();
        await End(planId, !approve ? ConfirmationOutcome.Denied : human ? ConfirmationOutcome.Approved : ConfirmationOutcome.Denied,
            !approve ? ConfirmationDenyReason.ByPerson : human ? ConfirmationDenyReason.None : ConfirmationDenyReason.SelfApproval, principal);
    }

    public async Task ResolveAsRequester(string planId, bool approve)
    {
        RequireOwnCaller(required: false);
        await Expire();
        await End(planId, ConfirmationOutcome.Denied,
            approve ? ConfirmationDenyReason.SelfApproval : ConfirmationDenyReason.ByPerson, Requester("requester"));
    }

    public Task Cancel(string planId)
    {
        RequireOwnCaller(required: false);
        return End(planId, ConfirmationOutcome.Denied, ConfirmationDenyReason.PlanInvalid, null);
    }

    protected override Task OnReminder(string name, CancellationToken cancellationToken) => name == ExpiryTimer ? Expire() : Task.CompletedTask;

    private async Task End(string planId, ConfirmationOutcome outcome, ConfirmationDenyReason reason, ConfirmationPrincipal? approver)
    {
        var index = Array.FindIndex(State.Entries, entry => entry.Request.PlanId == planId);
        if (index < 0 || State.Entries[index].Result is not null) { return; }
        var entry = State.Entries[index];
        var result = new ConfirmationResult(planId, outcome, reason, false);
        var entries = State.Entries.ToArray();
        entries[index] = entry with { Result = result };
        await Save(State with { Revision = State.Revision + 1, Entries = BoundEntries(entries) }, ConfirmationAudit.Record(entry.Request, result, approver, Clock.GetUtcNow()));
        Schedule();
    }

    private async Task Expire()
    {
        var now = Clock.GetUtcNow();
        foreach (var entry in State.Entries.Where(entry => entry.Result is null && entry.Request.ExpiresAt <= now).ToArray())
        {
            await End(entry.Request.PlanId, ConfirmationOutcome.Expired, ConfirmationDenyReason.Expired, null);
        }
    }

    private void Schedule()
    {
        var pending = State.Entries.Where(entry => entry.Result is null).ToArray();
        if (pending.Length == 0) { Context.Timers.Stop(ExpiryTimer); return; }
        var due = pending.Min(entry => entry.Request.ExpiresAt) - Clock.GetUtcNow();
        Context.Timers.Start(ExpiryTimer, due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1), null, keepAlive: true);
    }

    private static ConfirmationPrincipal Requester(string origin)
    {
        if (!RuntimeCallerContext.TryGet(out var caller)) { return ConfirmationPrincipal.App(origin); }
        return caller.Kind switch
        {
            CallerKind.User when !RuntimeCallerContext.IsModelOriginated(caller) => ConfirmationPrincipal.Person(caller.PrincipalId),
            CallerKind.App => ConfirmationPrincipal.App(caller.PrincipalId),
            _ => ConfirmationPrincipal.Agent(caller.PrincipalId),
        };
    }

    private static ConfirmationPrincipal Approver(CapabilityCaller caller)
        => caller.Kind == CallerKind.App ? ConfirmationPrincipal.App(caller.PrincipalId) : ConfirmationPrincipal.Agent(caller.PrincipalId);

    private CapabilityCaller? RequireOwnCaller(bool required)
    {
        if (!RuntimeCallerContext.TryGet(out var caller))
        {
            if (required) { throw new UnauthorizedAccessException("A trusted caller is required."); }
            return null;
        }
        if (!RuntimeCallerContext.IsTrusted(caller) || caller.BrainId != Id.Brain.Value)
        {
            throw new UnauthorizedAccessException("This confirmation belongs to another brain.");
        }
        return caller;
    }

    private static ConfirmationEntry[] BoundEntries(ConfirmationEntry[] entries)
    {
        if (entries.Length <= MaxEntries) { return entries; }
        var pending = entries.Where(e => e.Result is null).ToList();
        var completed = entries.Where(e => e.Result is not null).ToList();
        var allowedCompleted = Math.Max(0, MaxEntries - pending.Count);
        var keptPlanIds = new HashSet<string>(
            (completed.Count > allowedCompleted ? completed.TakeLast(allowedCompleted) : completed)
                .Select(e => e.Request.PlanId),
            StringComparer.Ordinal);
        return entries.Where(e => e.Result is null || keptPlanIds.Contains(e.Request.PlanId)).ToArray();
    }
}
