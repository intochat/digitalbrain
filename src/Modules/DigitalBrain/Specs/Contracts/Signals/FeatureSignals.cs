using DigitalBrain.Contracts;

namespace DigitalBrain.Specs.Signals;

[GenerateSerializer, Alias("specs.feature-changed")]
public sealed record FeatureChanged([property: Id(0)] string FeatureId, [property: Id(1)] long Revision, [property: Id(2)] bool FullyBound) : Signal;

[GenerateSerializer, Alias("specs.scenario-finished")]
public sealed record ScenarioFinished([property: Id(0)] string FeatureId, [property: Id(1)] ScenarioResult Result) : Signal;

[GenerateSerializer, Alias("specs.feature-verified")]
public sealed record FeatureVerified([property: Id(0)] string FeatureId, [property: Id(1)] long Revision, [property: Id(2)] bool Green) : Signal;
