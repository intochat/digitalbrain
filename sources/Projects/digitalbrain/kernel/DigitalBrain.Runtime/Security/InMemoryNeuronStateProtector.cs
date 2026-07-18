namespace DigitalBrain.Runtime.Security;

public sealed class InMemoryNeuronStateProtector : INeuronStateProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;
    public byte[] Unprotect(byte[] ciphertext) => ciphertext;
}
