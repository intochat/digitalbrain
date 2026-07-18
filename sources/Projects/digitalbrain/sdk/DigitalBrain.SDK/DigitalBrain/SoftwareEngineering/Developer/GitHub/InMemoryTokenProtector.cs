namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub;

public sealed class InMemoryTokenProtector : ITokenProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;
    public byte[] Unprotect(byte[] ciphertext) => ciphertext;
}
