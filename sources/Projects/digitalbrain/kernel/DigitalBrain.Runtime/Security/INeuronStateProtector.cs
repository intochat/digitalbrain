namespace DigitalBrain.Runtime.Security;

public interface INeuronStateProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

public sealed class PassThroughNeuronStateProtector : INeuronStateProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;
    public byte[] Unprotect(byte[] ciphertext) => ciphertext;
}
