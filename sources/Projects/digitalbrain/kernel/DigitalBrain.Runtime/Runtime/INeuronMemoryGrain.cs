namespace DigitalBrain.Runtime.Runtime;

public interface INeuronMemoryGrain : IGrainWithStringKey
{
    Task<byte[]?> GetEncryptedMemoryAsync();
    Task SaveEncryptedMemoryAsync(byte[] encryptedBytes);
}
