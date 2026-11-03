using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Capabilities.Connections.Sealing;

// Seals and opens connection credentials under a brain's core part key (R130: circle brain connection sharing).
// The owner's own connections stay under the master key via ISecretSealer; a shared copy's secrets are sealed here
// with KeyId "brain:{brainId}" so any machine holding that brain's core part opens them.
public interface IBrainSecretSealer
{
    Task<SealedSecret> Seal(string brainId, string name, string plaintext);
    Task<string> Open(string brainId, SealedSecret secret);
}

public static class BrainSeal
{
    public const string KeyPrefix = "brain:";

    public static string KeyId(string brainId) => KeyPrefix + brainId;

    public static bool IsBrainSealed(SealedSecret secret)
        => secret?.KeyId?.StartsWith(KeyPrefix, StringComparison.Ordinal) == true;
}
