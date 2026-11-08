using System.Globalization;

namespace DigitalBrain.UI.Confirmation;

/// <summary>
/// The confirmation card (07 D-11, D-14, D-16, D-19): a <see cref="Card"/> with the plan title and body, the static line
/// "This request expires in 5 minutes." and an <see cref="ApprovalCardNode"/> that resolves the plan by id.
/// Approve and deny are drawn by the OS inbox surface from the node's <see cref="Resolve"/>; they are never an <see cref="Emit"/>.
/// </summary>
public static class ConfirmView
{
    /// <summary>The runtime confirmations neuron key; equal to <c>DigitalBrain.Confirmation.IConfirmations.ConfirmationsNeuronKey</c>.</summary>
    public const string ConfirmationsNeuronKey = "confirmations";

    /// <summary>The operation both buttons call.</summary>
    public const string ResolveOperation = "Resolve";

    /// <summary>The line shown to a viewer who cannot approve (an app or agent identity).</summary>
    public const string WaitingText = "Waiting for you to approve.";

    /// <summary>The key of the approval node in the card.</summary>
    public const string ApprovalKey = "confirm";

    /// <summary>The card for a pending plan.</summary>
    /// <param name="planId">The plan id (<c>ConfirmationRequested.PlanId</c>).</param>
    /// <param name="title">The plan title.</param>
    /// <param name="body">The plan body.</param>
    /// <param name="expiry">The plan expiry; shown as whole minutes, without ticking.</param>
    /// <param name="viewerIsHuman">False for an app or agent viewer: the approval cannot be resolved (no resolution) and <see cref="WaitingText"/> is added.</param>
    public static Card For(string planId, string title, string body, TimeSpan expiry, bool viewerIsHuman)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        var minutes = Math.Max(1, (int)Math.Round(expiry.TotalMinutes));
        var children = new List<Node>
        {
            new Text(string.Create(CultureInfo.InvariantCulture, $"This request expires in {minutes} {(minutes == 1 ? "minute" : "minutes")}.")),
        };
        if (!viewerIsHuman) { children.Add(new Text(WaitingText)); }
        children.Add(new ApprovalCardNode(ApprovalKey, planId, title, body, [], Resolve: viewerIsHuman ? new Resolve(planId) : null));
        return new Card(title, body, children);
    }
}
