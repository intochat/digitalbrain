using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Core.Enforcement;

// One ordered increment of the single enforcement point. A stage returns null to abstain or a
// decision to settle the request. P2 registers only the grant stage; allowances and limits arrive
// as further stages in P3 without changing this pipeline or the filter that runs them.
public interface ICallFilterStage
{
    ValueTask<CallDecision?> EvaluateAsync(CallRequest request, CancellationToken cancellationToken = default);
}
