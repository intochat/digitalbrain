namespace DigitalBrain.Security;

internal interface IDurablePayloadProtector
{
    byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload);
}
