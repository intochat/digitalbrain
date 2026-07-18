using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.Microsoft.Aspire.Runtime;

[GrainType("DigitalBrain.SDK.Aspire.Runtime.IsFlutterWebPredicate")]
public sealed class IsFlutterWebPredicateGrain : Grain, IPredicateNeuronTarget
{
    public Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        bool matches = string.Equals(subject, "flutter-web", StringComparison.OrdinalIgnoreCase);
        bool expected = string.Equals(target, "true", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(matches == expected);
    }
}
