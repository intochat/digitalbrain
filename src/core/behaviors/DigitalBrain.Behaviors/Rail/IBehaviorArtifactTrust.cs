namespace DigitalBrain.Behaviors;

public interface IBehaviorArtifactTrust
{
    byte[] Sign(string artifactHash);

    void Verify(string artifactHash, ReadOnlySpan<byte> signature);
}
