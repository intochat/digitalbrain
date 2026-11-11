using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;

namespace DigitalBrain.Testing;

internal sealed class BrainConfirmationRequests(TestConfirmations confirmations, TestOS os, BrainId brain) : IConfirmationRequests
{
    private readonly NeuronId _publisher = IConfirmationRequests.Of(brain);

    public async Task<ConfirmationResult?> Request(ConfirmationPlan plan)
    {
        var caller = TestCallerAmbient.Instance.Current;
        var requester = caller is not null ? confirmations.Current : ConfirmationPrincipal.App(_publisher.ToString());
        var raisedBy = caller?.Via?.ToString() ?? caller?.Actor.ToString() ?? "edge";
        return await confirmations.Request(plan, requester, raisedBy, PublishToBrain).ConfigureAwait(false);
    }

    public Task<ConfirmationResult?> Outcome(string planId) => confirmations.Outcome(planId);

    public Task Withdraw(string planId) => confirmations.Withdraw(planId);

    private async Task PublishToBrain(Signal signal)
    {
        var journal = os.Journal(brain);
        var nudge = os.Delivery.NudgeFor(brain);
        var stamp = TestCause.Stamp();
        var commit = new Commit(_publisher, null, [signal], null, new Stamp(stamp.TraceId, stamp.CausationId));
        var result = await journal.Append(commit, CancellationToken.None).ConfigureAwait(false);
        var lastPos = new Position(result.FirstPosition.Value);
        await nudge.Appended(_publisher, result.Journaled(commit), lastPos, CancellationToken.None).ConfigureAwait(false);
    }
}
