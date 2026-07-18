namespace DigitalBrain.SDK.DigitalBrain.Mcp;

public sealed record CardView(string RootWidget, string DataJson);

public sealed record CodeBundle(string ImplCode, string StepsCode);

// Stable contract returned by the `brain` tool. Shaped so a future
// progress-streaming transport can be added without changing this record.
public sealed record BrainResult(
    string Outcome,                 // promoted | failed | timeout | unavailable
    string? NeuronId,
    string? FeatureText,
    CodeBundle? Code,
    string? TestResult,
    IReadOnlyList<CardView> Cards);
