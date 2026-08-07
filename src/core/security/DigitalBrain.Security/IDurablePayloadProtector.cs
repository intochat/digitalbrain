namespace DigitalBrain.Security;

public interface IDurablePayloadProtector
{
    byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload);
}
