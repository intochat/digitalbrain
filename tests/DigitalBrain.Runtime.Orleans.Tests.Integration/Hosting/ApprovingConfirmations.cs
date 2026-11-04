using DigitalBrain.Confirmation;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

// The confirmation gate of the integration silos: every plan is approved as a person at once, so an install needs no screen.
// HostNeuronContext prefers a registered IConfirmations over UnavailableConfirmations.
internal sealed class ApprovingConfirmations : IConfirmations
{
    public ConfirmationPolicy Policy => ConfirmationPolicy.DestructiveOnly;

    public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.FromResult(new ConfirmationResult(plan.Id, ConfirmationOutcome.Approved, ConfirmationDenyReason.None, Skipped: false));
    }

    public Task Resolve(string planId, bool approve, CancellationToken ct = default) => Task.CompletedTask;
}
