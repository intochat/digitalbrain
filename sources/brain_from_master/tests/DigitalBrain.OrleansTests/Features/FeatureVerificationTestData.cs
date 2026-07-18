using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

internal static class FeatureVerificationTestData
{
    public static FeatureVerification Passing(
        ReleaseDigest release,
        FeatureSourceSnapshot source,
        int total,
        DateTimeOffset verifiedAt)
    {
        var scenarios = Enumerable.Range(0, total)
            .Select(index => new FeatureScenarioEvidence(
                $"scenario-{index}",
                $"Scenario {index}",
                FeatureScenarioOutcome.Passed,
                null,
                0))
            .ToArray();
        var evidence = new FeatureVerificationEvidence(
            FeatureDraftAuthoringTransitions.SourceReference(source),
            total,
            total,
            0,
            0,
            scenarios,
            [new FeatureVerificationArtifact("scenarios.json", "application/json", 1, $"sha256:{new string('f', 64)}")]);
        return new FeatureVerification(release, total, total, 0, 0, verifiedAt, evidence);
    }
}
