using System.Text;

namespace DigitalBrain.Abstractions.Secrets;

public interface IDigitalBrainSecretStore
{
    Task StoreAsync(SecretKeyPath path, byte[] plaintext, CancellationToken cancellationToken = default);

    Task<byte[]?> ReadAsync(SecretKeyPath path, CancellationToken cancellationToken = default);

    Task DeleteAsync(SecretKeyPath path, CancellationToken cancellationToken = default);
}

public static class DigitalBrainSecretStoreExtensions
{
    public static Task StoreStringAsync(
        this IDigitalBrainSecretStore store,
        SecretKeyPath path,
        string plaintext,
        CancellationToken cancellationToken = default) =>
        store.StoreAsync(path, Encoding.UTF8.GetBytes(plaintext), cancellationToken);

    public static async Task<string?> ReadStringAsync(
        this IDigitalBrainSecretStore store,
        SecretKeyPath path,
        CancellationToken cancellationToken = default)
    {
        var plaintext = await store.ReadAsync(path, cancellationToken);
        return plaintext is null ? null : Encoding.UTF8.GetString(plaintext);
    }
}

