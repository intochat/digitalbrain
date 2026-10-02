namespace DigitalBrain.Platform.Secrets;

// Wraps the per-owner data key so only ciphertext is persisted. The platform registers the
// master-key wrapper and refuses to start without a configured master key.
public interface IKeyWrapper
{
    string Wrap(byte[] key);

    byte[] Unwrap(string wrapped);
}
