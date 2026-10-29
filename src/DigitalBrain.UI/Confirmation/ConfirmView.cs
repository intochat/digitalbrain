using System.Globalization;

namespace DigitalBrain.UI.Confirmation;

/// <summary>
/// The confirmation card (07 D-11, D-14, D-16, D-19): a <see cref="Card"/> with the plan's title and body, the static line
/// "This request expires in 5 minutes." and two <see cref="Button"/>s. Approve is <see cref="ButtonTone.Accent"/>, deny is
/// <see cref="ButtonTone.Neutral"/>; there is no destructive tone. Both buttons call operation <see cref="ResolveOperation"/> of the
/// runtime neuron <see cref="ConfirmationsNeuronKey"/> with the plan id and the approve flag, never the requesting app's neuron,
/// so the click is dispatched to the runtime and stamped with the person viewing it. Built from value-tree nodes only: no new Dart.
/// </summary>
public static class ConfirmView
{
    /// <summary>The runtime confirmations neuron key; equal to <c>DigitalBrain.Confirmation.IConfirmations.ConfirmationsNeuronKey</c>.</summary>
    public const string ConfirmationsNeuronKey = "confirmations";

    /// <summary>The operation both buttons call.</summary>
    public const string ResolveOperation = "Resolve";

    /// <summary>The line shown to a viewer who cannot approve (an app or agent identity).</summary>
    public const string WaitingText = "Waiting for you to approve.";

    /// <summary>The card for a pending plan.</summary>
    /// <param name="planId">The plan id (<c>ConfirmationRequested.PlanId</c>).</param>
    /// <param name="title">The plan title.</param>
    /// <param name="body">The plan body.</param>
    /// <param name="approveLabel">The approve label, for example "Delete".</param>
    /// <param name="denyLabel">The deny label, for example "Keep".</param>
    /// <param name="expiry">The plan expiry; shown as whole minutes, without ticking.</param>
    /// <param name="viewerIsHuman">False for an app or agent viewer: approve is disabled and <see cref="WaitingText"/> is added.</param>
    public static Card For(string planId, string title, string body, string approveLabel, string denyLabel, TimeSpan expiry, bool viewerIsHuman)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(approveLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(denyLabel);
        var minutes = Math.Max(1, (int)Math.Round(expiry.TotalMinutes));
        var children = new List<Node>
        {
            new Text(string.Create(CultureInfo.InvariantCulture, $"This request expires in {minutes} {(minutes == 1 ? "minute" : "minutes")}.")),
        };
        if (!viewerIsHuman) { children.Add(new Text(WaitingText)); }
        children.Add(new Button(approveLabel, Resolve(planId, approve: true), Enabled: viewerIsHuman, Tone: ButtonTone.Accent));
        children.Add(new Button(denyLabel, Resolve(planId, approve: false), Enabled: true, Tone: ButtonTone.Neutral));
        return new Card(title, body, children);
    }

    /// <summary>The action of an approve (<paramref name="approve"/> true) or deny button for <paramref name="planId"/>.</summary>
    public static ButtonAction Resolve(string planId, bool approve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        return new ButtonAction(ConfirmationsNeuronKey, ResolveOperation, [planId, approve ? "true" : "false"]);
    }
}
