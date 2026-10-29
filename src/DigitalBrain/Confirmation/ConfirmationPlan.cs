namespace DigitalBrain.Confirmation;

/// <summary>What a confirmation asks about.</summary>
public enum ConfirmationAction
{
    /// <summary>Delete an item.</summary>
    Delete,

    /// <summary>Move an item.</summary>
    Move,

    /// <summary>Copy an item.</summary>
    Copy,

    /// <summary>Rename an item.</summary>
    Rename,

    /// <summary>Replace an existing item.</summary>
    Overwrite,

    /// <summary>Anything else.</summary>
    Other,
}

/// <summary>
/// A question for a person before something happens. Every text is shown as is; <see cref="TargetLabel"/> is a display name only,
/// never a path (T-07-08).
/// </summary>
/// <param name="Id">The plan id; unique per brain.</param>
/// <param name="Action">What would happen.</param>
/// <param name="Destructive">True when it cannot be undone (delete, move across connections, overwrite).</param>
/// <param name="Title">The card title, for example "Delete report.pdf?".</param>
/// <param name="Body">The card body.</param>
/// <param name="ApproveLabel">The approve button label, for example "Delete".</param>
/// <param name="DenyLabel">The deny button label, for example "Keep".</param>
/// <param name="TargetLabel">The display name of what is acted on.</param>
public sealed record ConfirmationPlan(
    string Id,
    ConfirmationAction Action,
    bool Destructive,
    string Title,
    string Body,
    string ApproveLabel,
    string DenyLabel,
    string TargetLabel)
{
    /// <summary>How long the question stays open; 5 minutes unless set (07 D-16).</summary>
    public TimeSpan Expiry { get; init; } = ConfirmationRules.DefaultExpiry;
}

/// <summary>How <see cref="IConfirmations.Confirm"/> ended.</summary>
/// <param name="PlanId">The plan id.</param>
/// <param name="Outcome">Approved, denied or expired.</param>
/// <param name="Reason">Why it was not approved; <see cref="ConfirmationDenyReason.None"/> when approved.</param>
/// <param name="Skipped">True when the policy approved without asking.</param>
public sealed record ConfirmationResult(string PlanId, ConfirmationOutcome Outcome, ConfirmationDenyReason Reason, bool Skipped)
{
    /// <summary>True when approved.</summary>
    public bool Approved => Outcome == ConfirmationOutcome.Approved;
}
