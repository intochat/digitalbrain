using DigitalBrain.Confirmation;
using DigitalBrain.State;

namespace DigitalBrain.Runtime.Orleans.Confirmations;

public sealed record ConfirmationEntry(ConfirmationRequested Request, ConfirmationResult? Result = null);

[ExportableState]
public sealed record ConfirmationState
{
    public long Revision { get; init; }
    public ConfirmationEntry[] Entries { get; init; } = [];
}

/// <summary>
/// The append-only confirmation audit. One <see cref="ConfirmationResolved"/> per outcome, journaled with the
/// state change. The record names the target by <see cref="ConfirmationPlan.TargetLabel"/> only.
/// </summary>
internal static class ConfirmationAudit
{
    public static ConfirmationResolved Record(ConfirmationRequested request, ConfirmationResult result, ConfirmationPrincipal? approver, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        return new(request.PlanId, request.Plan.Action, request.Plan.TargetLabel, request.Requester, approver, request.Policy,
            result.Outcome, result.Reason, result.Skipped, at);
    }
}
