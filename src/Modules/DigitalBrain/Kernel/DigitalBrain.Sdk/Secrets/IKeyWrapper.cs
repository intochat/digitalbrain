namespace DigitalBrain.Sdk.Secrets;

// Wraps the per-owner data key so only ciphertext is persisted. Local runs use DPAPI; hosted runs
// substitute the Key Vault adapter backed by IKeyVault.
public interface IKeyWrapper
{
    string Wrap(byte[] key);

    byte[] Unwrap(string wrapped);
}
