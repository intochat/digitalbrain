using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Compute.Allowances;

// The enforcement decision is a pure function of a snapshot so it can be tested without Orleans.
// The grain loads the snapshot, evaluates it, then persists the reservation or the pending
// approval the outcome names.
internal sealed record AllowanceSnapshot(
    IReadOnlyList<Allowance> Allowances,
    IReadOnlyList<AllowanceReservation> Reservations,
    LimitPolicy Limits,
    string PriceBookVersion,
    LimitAlert HighestAlert,
    DateTimeOffset? HardStoppedAt);

internal sealed record AllowanceOutcome(
    AllowanceDecision Decision,
    LimitAlert Alert,
    bool HardStop,
    decimal Spent,
    decimal Cap);

internal static class AllowanceRules
{
    internal static readonly TimeSpan ReadExportGrace = TimeSpan.FromDays(30);

    internal static decimal Effective(AllowanceReservation reservation)
        => reservation.Settled ? reservation.SettledCompute : reservation.ReservedCompute;

    internal static string ReservationId(CallRequest request)
        => $"{request.Caller.IntentId ?? "anon"}:{request.TargetNeuron}:{request.Operation}";

    internal static string ApprovalId(CallRequest request)
        => $"approve:{request.Caller.WorkspaceId}:{request.Caller.AppId ?? "none"}:{request.Operation}";

    internal static bool IsWithinGrace(DateTimeOffset? hardStoppedAt, DateTimeOffset now)
        => hardStoppedAt is { } stopped && now <= stopped + ReadExportGrace;

    internal static bool ShouldCharge(FailureClass failure) => failure switch
    {
        FailureClass.PlatformFault or FailureClass.ProviderOutage or FailureClass.ThirdPartyFault => false,
        _ => true,
    };

    internal static decimal CapSettlement(decimal actualCompute, decimal spentExcludingReservation, decimal cap)
    {
        if (cap <= 0) { return actualCompute; }
        var headroom = cap - spentExcludingReservation;
        return headroom <= 0 ? 0m : Math.Min(actualCompute, headroom);
    }

    internal static AllowanceOutcome Evaluate(CallRequest request, AllowanceSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var caller = request.Caller;
        var isReadOnly = !request.HasSideEffects && request.EstimatedCompute <= 0;

        if (!isReadOnly && request.EstimatedCompute <= 0)
        {
            return Outcome(AllowanceDecision.Allow(), snapshot.HighestAlert, false, 0m, 0m);
        }

        var assessment = AssessLimits(request, snapshot, request.EstimatedCompute);
        if (isReadOnly)
        {
            return EvaluateReadOnly(snapshot, now, assessment);
        }

        if (assessment.Exceeded)
        {
            return Outcome(Deny(CallDenial.LimitReached,
                $"The call would spend {request.EstimatedCompute} past the {assessment.Cap} cap."),
                assessment.Alert, snapshot.HardStoppedAt is not null, assessment.Spent, assessment.Cap);
        }

        var selection = SelectAllowance(request, snapshot, now);
        if (selection.Allowance is null)
        {
            return Outcome(new AllowanceDecision
            {
                Allowed = false,
                Denial = CallDenial.MissingAllowance,
                Explanation = selection.ReconsentRequired
                    ? "The app's permissions or prices changed; re-consent is required."
                    : "No allowance covers this paid call; approval is pending.",
                ApprovalId = ApprovalId(request),
                RemainingCompute = 0m,
            }, assessment.Alert, false, assessment.Spent, assessment.Cap);
        }

        if (selection.Remaining < request.EstimatedCompute)
        {
            return Outcome(Deny(CallDenial.LimitReached,
                $"The allowance has {selection.Remaining} Compute left and the call estimates {request.EstimatedCompute}."),
                assessment.Alert, true, assessment.Spent, assessment.Cap);
        }

        var reachAfter = assessment.Cap > 0 && (assessment.Spent + request.EstimatedCompute) >= assessment.Cap;
        return Outcome(new AllowanceDecision
        {
            Allowed = true,
            ReservationId = ReservationId(request),
            AllowanceId = selection.Allowance.AllowanceId,
            RemainingCompute = selection.Remaining - request.EstimatedCompute,
        }, assessment.Alert, snapshot.HardStoppedAt is not null || reachAfter, assessment.Spent, assessment.Cap);
    }

    private static AllowanceOutcome EvaluateReadOnly(AllowanceSnapshot snapshot, DateTimeOffset now, LimitAssessment assessment)
    {
        if (snapshot.HardStoppedAt is null)
        {
            return Outcome(AllowanceDecision.Allow(), assessment.Alert, false, assessment.Spent, assessment.Cap);
        }

        return IsWithinGrace(snapshot.HardStoppedAt, now)
            ? Outcome(AllowanceDecision.Allow(), assessment.Alert, true, assessment.Spent, assessment.Cap)
            : Outcome(Deny(CallDenial.LimitReached, "The 30-day read/export grace has expired."), assessment.Alert, true, assessment.Spent, assessment.Cap);
    }

    private static AllowanceSelection SelectAllowance(CallRequest request, AllowanceSnapshot snapshot, DateTimeOffset now)
    {
        var caller = request.Caller;
        Allowance? best = null;
        decimal bestRemaining = 0m;
        var reconsent = false;

        foreach (var allowance in snapshot.Allowances)
        {
            if (!ContextMatches(allowance, request)) { continue; }
            if (allowance.Revoked) { continue; }
            if (!ScopeAvailable(allowance, caller, snapshot.Reservations)) { continue; }
            if (allowance.PriceBookVersion != snapshot.PriceBookVersion
                || !PermissionsCovered(allowance, request.SemanticTypeIds))
            {
                reconsent = true;
                continue;
            }

            var limit = allowance.Level == ApprovalLevel.InstallConsent && allowance.MonthlyLimitCompute > 0
                ? allowance.MonthlyLimitCompute
                : allowance.LimitCompute;
            var spent = SpendAgainst(allowance, snapshot.Reservations, now);
            var remaining = limit - spent;
            if (best is null || allowance.Level > best.Level)
            {
                best = allowance;
                bestRemaining = remaining;
            }
        }

        return new AllowanceSelection(best, bestRemaining, reconsent);
    }

    private static bool ContextMatches(Allowance allowance, CallRequest request)
    {
        var caller = request.Caller;
        if (!string.Equals(allowance.WorkspaceId, caller.WorkspaceId, StringComparison.Ordinal)) { return false; }
        if (allowance.AppId is { Length: > 0 } allowedApp
            && !string.Equals(allowedApp, caller.AppId, StringComparison.Ordinal)) { return false; }
        return allowance.Operation is not { Length: > 0 } operation
            || string.Equals(operation, request.Operation, StringComparison.Ordinal);
    }

    private static bool ScopeAvailable(Allowance allowance, CallerContext caller, IReadOnlyList<AllowanceReservation> reservations)
        => allowance.Scope switch
        {
            AllowanceScope.ThisChat => caller.ConversationId is { Length: > 0 }
                && string.Equals(allowance.ConversationId, caller.ConversationId, StringComparison.Ordinal),
            AllowanceScope.Once => !reservations.Any(reservation =>
                string.Equals(reservation.AllowanceId, allowance.AllowanceId, StringComparison.Ordinal)),
            _ => true,
        };

    private static bool PermissionsCovered(Allowance allowance, IReadOnlyList<string> semanticTypeIds)
        => semanticTypeIds.All(id => allowance.Permissions.Contains(id, StringComparer.Ordinal));

    private static decimal SpendAgainst(Allowance allowance, IReadOnlyList<AllowanceReservation> reservations, DateTimeOffset now)
    {
        var monthly = allowance.Level == ApprovalLevel.InstallConsent && allowance.MonthlyLimitCompute > 0;
        return reservations
            .Where(reservation => string.Equals(reservation.AllowanceId, allowance.AllowanceId, StringComparison.Ordinal))
            .Where(reservation => !monthly || SameMonth(reservation.OccurredAt, now))
            .Sum(Effective);
    }

    private static bool SameMonth(DateTimeOffset left, DateTimeOffset right)
        => left.Year == right.Year && left.Month == right.Month;

    private static LimitAssessment AssessLimits(CallRequest request, AllowanceSnapshot snapshot, decimal amount)
    {
        var caller = request.Caller;
        var policy = snapshot.Limits;
        var candidates = new List<LimitAssessment>();

        Add(policy.AccountLimitCompute, Sum(snapshot.Reservations, _ => true));
        Add(policy.WorkspaceLimitCompute, Sum(snapshot.Reservations,
            reservation => string.Equals(reservation.WorkspaceId, caller.WorkspaceId, StringComparison.Ordinal)));
        if (caller.AppId is { Length: > 0 })
        {
            Add(policy.AppLimitCompute, Sum(snapshot.Reservations,
                reservation => string.Equals(reservation.AppId, caller.AppId, StringComparison.Ordinal)));
        }
        if (caller.IntentId is { Length: > 0 })
        {
            Add(policy.RunLimitCompute, Sum(snapshot.Reservations,
                reservation => string.Equals(reservation.IntentId, caller.IntentId, StringComparison.Ordinal)));
        }

        if (candidates.Count == 0) { return new LimitAssessment(0m, 0m, 0m, false, LimitAlert.None); }
        return candidates.OrderByDescending(candidate => candidate.Utilization).First();

        void Add(decimal cap, decimal spent)
        {
            if (cap <= 0) { return; }
            var projected = spent + amount;
            var utilization = projected / cap;
            var alert = Threshold(utilization);
            candidates.Add(new LimitAssessment(spent, cap, utilization, projected > cap, alert));
        }
    }

    private static decimal Sum(IReadOnlyList<AllowanceReservation> reservations, Func<AllowanceReservation, bool> predicate)
        => reservations.Where(predicate).Sum(Effective);

    private static LimitAlert Threshold(decimal utilization) => utilization switch
    {
        >= 1m => LimitAlert.At100,
        >= 0.9m => LimitAlert.At90,
        >= 0.75m => LimitAlert.At75,
        _ => LimitAlert.None,
    };

    private static AllowanceDecision Deny(CallDenial denial, string explanation) => new()
    {
        Allowed = false,
        Denial = denial,
        Explanation = explanation,
    };

    private static AllowanceOutcome Outcome(AllowanceDecision decision, LimitAlert alert, bool hardStop, decimal spent, decimal cap)
        => new(decision, alert, hardStop, spent, cap);

    private sealed record AllowanceSelection(Allowance? Allowance, decimal Remaining, bool ReconsentRequired);

    private sealed record LimitAssessment(decimal Spent, decimal Cap, decimal Utilization, bool Exceeded, LimitAlert Alert);
}
