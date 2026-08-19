namespace DigitalBrain.Core;

public interface IDurablePayloadProtector
{
    byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload);
}