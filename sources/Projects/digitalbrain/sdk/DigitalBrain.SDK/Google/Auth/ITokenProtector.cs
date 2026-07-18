namespace DigitalBrain.SDK.Google.Auth;

public interface ITokenProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
