using DigitalBrain.Confirmation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Operations;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

// The confirmation gate of the integration silos: every plan is approved as a person at once, and the resolution is journaled as
// ConfirmationResolved by a test confirmations neuron of the brain, as the real confirmations neuron publishes it, so the installs
// neuron's reaction completes a pending install. HostNeuronContext prefers a registered IConfirmationContexts.
internal sealed class ApprovingConfirmations(IServiceProvider services) : IConfirmationContexts
{
    public IConfirmations For(BrainId brain, string requester) => new Approver(services, brain, requester);

    private sealed class Approver(IServiceProvider services, BrainId brain, string requester) : IConfirmations
    {
        public ConfirmationPolicy Policy => ConfirmationPolicy.DestructiveOnly;

        public async Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            var resolved = new ConfirmationResolved(plan.Id, plan.Action, plan.TargetLabel, ConfirmationPrincipal.App(requester),
                ConfirmationPrincipal.Person("fixture"), Policy, ConfirmationOutcome.Approved, ConfirmationDenyReason.None, Skipped: false, DateTimeOffset.UtcNow);
            await NeuronHosting.Get<ITestConfirmer>(services, new NeuronId(brain, TestConfirmer.Type, "default")).Resolved(resolved);
            return new ConfirmationResult(plan.Id, ConfirmationOutcome.Approved, ConfirmationDenyReason.None, Skipped: false);
        }

        public Task Resolve(string planId, bool approve, CancellationToken ct = default) => Task.CompletedTask;
    }
}

public interface ITestConfirmer
{
    Task Resolved(ConfirmationResolved resolved);
}

// Journals the fixture's resolutions in the brain, as the confirmations neuron of the os side does.
[Neuron(Type)]
public sealed class TestConfirmer : Neuron, ITestConfirmer
{
    internal const string Type = "test.confirmations";

    public Task Resolved(ConfirmationResolved resolved) => Publish(resolved);
}
