using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Identity.Identity;

[GrainType("DigitalBrain.SDK.Identity.EqualityStore")]
public sealed class EqualityStoreGrain : Grain, IPredicateNeuronTarget
{
    public Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        bool matches = string.Equals(subject, target, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(matches);
    }
}
