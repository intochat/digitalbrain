namespace DigitalBrain.SDK.Google.Auth;

// Test-only pass-through used when DigitalBrain:Google:UseStubServices=true.
// Never registered in production; the bridge throws on unsupported OS instead.
internal sealed class InMemoryTokenProtector : ITokenProtector
{
    public byte[] Protect(byte[] plaintext) => (byte[])plaintext.Clone();
    public byte[] Unprotect(byte[] ciphertext) => (byte[])ciphertext.Clone();
}
