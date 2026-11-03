using System.Security.Cryptography;
using DigitalBrain.Identity.Local.Custody;

namespace DigitalBrain.Identity.Local.Tests.Unit;

public sealed class FileMasterKeyCustodyFacts : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "db-custody-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void WrapThenUnwrapRoundTrips()
    {
        var custody = new FileMasterKeyCustody(_directory);
        byte[] secret = [1, 2, 3, 4, 5, 6, 7, 8];

        var wrapped = custody.Wrap("acct-1", "vault-key|b1", secret);
        var unwrapped = custody.Unwrap("acct-1", "vault-key|b1", wrapped);

        Assert.Equal(secret, unwrapped);
        Assert.NotEqual(secret, wrapped);
    }

    [Fact]
    public void UnwrapWithAnotherAccountFails()
    {
        var custody = new FileMasterKeyCustody(_directory);
        byte[] secret = [9, 10, 11, 12];

        var wrapped = custody.Wrap("acct-1", "vault-key|b1", secret);

        Assert.ThrowsAny<CryptographicException>(() => custody.Unwrap("acct-2", "vault-key|b1", wrapped));
    }

    [Fact]
    public void RootIsCreatedOnceAndReused()
    {
        var first = new FileMasterKeyCustody(_directory);
        var second = new FileMasterKeyCustody(_directory);
        byte[] secret = [42, 43, 44, 45];

        var wrappedByFirst = first.Wrap("acct-1", "ctx-1", secret);
        var unwrappedBySecond = second.Unwrap("acct-1", "ctx-1", wrappedByFirst);
        var wrappedBySecond = second.Wrap("acct-1", "ctx-2", secret);
        var unwrappedByFirst = first.Unwrap("acct-1", "ctx-2", wrappedBySecond);

        Assert.Equal(secret, unwrappedBySecond);
        Assert.Equal(secret, unwrappedByFirst);
        var keyPath = Path.Combine(_directory, "master-key");
        Assert.True(File.Exists(keyPath));
        Assert.Equal(32, File.ReadAllBytes(keyPath).Length);
    }
}
