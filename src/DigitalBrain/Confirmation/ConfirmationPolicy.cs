namespace DigitalBrain.Confirmation;

/// <summary>When a brain asks a person before an operation runs (07 D-03, D-22). Set per brain; the default value is <see cref="DestructiveOnly"/>.</summary>
public enum ConfirmationPolicy
{
    /// <summary>Ask only for destructive plans: delete, move across storage connections, overwrite. The default.</summary>
    DestructiveOnly = 0,

    /// <summary>Ask for every planned operation, destructive or not.</summary>
    Always = 1,

    /// <summary>Never ask; the plan is approved and the audit signal is still written.</summary>
    Never = 2,
}

/// <summary>How a confirmation ended.</summary>
public enum ConfirmationOutcome
{
    /// <summary>A person approved it, or the policy skipped the question.</summary>
    Approved,

    /// <summary>It was denied, by a person or by a rule.</summary>
    Denied,

    /// <summary>Nobody answered within the plan's expiry.</summary>
    Expired,
}

/// <summary>Why a confirmation did not end approved.</summary>
public enum ConfirmationDenyReason
{
    /// <summary>Not denied.</summary>
    None,

    /// <summary>A person chose deny.</summary>
    ByPerson,

    /// <summary>An app or agent tried to approve; only a human principal may approve (07 D-09).</summary>
    SelfApproval,

    /// <summary>Nobody answered in time.</summary>
    Expired,

    /// <summary>The plan was no longer valid when it was answered.</summary>
    PlanInvalid,
}

/// <summary>What kind of identity a principal is.</summary>
public enum PrincipalKind
{
    /// <summary>A human person.</summary>
    Person,

    /// <summary>An installed app.</summary>
    App,

    /// <summary>An agent acting for someone.</summary>
    Agent,
}

/// <summary>Who requested or answered a confirmation. The runtime stamps it from the caller; it is never a call argument.</summary>
/// <param name="Id">The principal id.</param>
/// <param name="Kind">Person, app or agent.</param>
public sealed record ConfirmationPrincipal(string Id, PrincipalKind Kind)
{
    /// <summary>True for a human person: the only kind that may approve (07 D-09).</summary>
    public bool IsHuman => Kind == PrincipalKind.Person;

    /// <summary>A human person.</summary>
    public static ConfirmationPrincipal Person(string id) => new(Named(id), PrincipalKind.Person);

    /// <summary>An app.</summary>
    public static ConfirmationPrincipal App(string id) => new(Named(id), PrincipalKind.App);

    /// <summary>An agent.</summary>
    public static ConfirmationPrincipal Agent(string id) => new(Named(id), PrincipalKind.Agent);

    private static string Named(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }
}

/// <summary>The rules every <see cref="IConfirmations"/> implementation shares.</summary>
public static class ConfirmationRules
{
    /// <summary>The expiry of a plan that names none: 5 minutes (07 D-16).</summary>
    public static TimeSpan DefaultExpiry { get; } = TimeSpan.FromMinutes(5);

    /// <summary>True when <paramref name="plan"/> must be put to a person under <paramref name="policy"/>.</summary>
    public static bool Asks(ConfirmationPolicy policy, ConfirmationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return policy switch
        {
            ConfirmationPolicy.Always => true,
            ConfirmationPolicy.Never => false,
            _ => plan.Destructive,
        };
    }
}
