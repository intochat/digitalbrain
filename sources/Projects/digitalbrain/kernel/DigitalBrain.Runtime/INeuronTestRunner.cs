namespace DigitalBrain.Runtime;

public sealed record TestResult(
    bool AllGreen,
    IReadOnlyList<string> Failures);

// Runs the .feature scenarios in a DynamicNeuronSpec against the staged
// DynamicNeuronGrain identified by the spec's NeuronId. Returns AllGreen
// only when every scenario's Then-step assertions pass. The Creator uses
// this result to gate promotion: green → promote, red → feedback-retry.
//
// Slice-2 day-zero supports a constrained Gherkin dialect:
//
//   Scenario: <name>
//     Given a fresh dynamic neuron
//     When the neuron is invoked with payload {...} as type "X.Y.Z"
//     Then the response equals {...}
//     And the response contains "subtext"
//
// Phase 4 may extend; the LLM prompt should be calibrated to produce only
// these patterns.
public interface INeuronTestRunner
{
    Task<TestResult> RunAsync(DynamicNeuronSpec spec, CancellationToken ct = default);
}
