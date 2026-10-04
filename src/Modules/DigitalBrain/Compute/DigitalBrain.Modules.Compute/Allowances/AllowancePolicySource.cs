using DigitalBrain.Contracts.Enforcement;
using Orleans;

namespace DigitalBrain.Compute.Allowances;

// The call filter only carries caller context; the allowance stage reads the durable ledger through
// this seam so the stage can be tested with a fake and the decision stays in AllowanceRules.
internal interface IAllowancePolicySource
{
    ValueTask<AllowanceDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken);
}

internal sealed class GrainAllowancePolicySource(IGrainFactory grains) : IAllowancePolicySource
{
    public ValueTask<AllowanceDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken)
    {
        var ledger = grains.GetGrain<IAllowanceLedger>(request.Caller.AccountId);
        // The ledger cannot meter calls to itself: consulting it from its own authorization
        // turn would be a self-call on a non-reentrant grain. Reading the ledger costs nothing.
        if (request.TargetNeuron == ledger.GetGrainId().ToString()) { return new(AllowanceDecision.Allow()); }
        return new(ledger.AuthorizeAsync(request, cancellationToken));
    }
}
