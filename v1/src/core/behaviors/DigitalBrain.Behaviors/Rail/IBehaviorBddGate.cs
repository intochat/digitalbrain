namespace DigitalBrain.Behaviors;

using DigitalBrain.Behaviors.Artifacts;

public interface IBehaviorBddGate
{
    BehaviorInstallTestReport Evaluate(
        BehaviorArtifactEnvelope envelope,
        ReadOnlyMemory<byte> assemblyBytes,
        string artifactHash,
        IBehaviorCapabilityResolver capabilities,
        TimeProvider time);
}
