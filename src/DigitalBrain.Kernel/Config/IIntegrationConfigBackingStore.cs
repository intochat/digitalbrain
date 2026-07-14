namespace DigitalBrain.Kernel.Config;

internal interface IIntegrationConfigBackingStore
{
    Task<byte[]?> LoadAsync(string scope, string pack, CancellationToken cancellationToken = default);
    Task SaveAsync(string scope, string pack, byte[] encryptedBlob, CancellationToken cancellationToken = default);
}
