namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub;

public interface ITokenProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
