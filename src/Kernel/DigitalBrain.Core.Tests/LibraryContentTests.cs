using DigitalBrain.Core;

namespace DigitalBrain.Core.Tests;

public sealed class LibraryContentTests
{
    [Fact]
    public void Hash_is_stable_lowercase_sha256_hex()
    {
        var hash = LibraryContent.Hash("""{"kind":"chart"}""");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Equal(LibraryContent.Hash("""{"kind":"chart"}"""), hash);
    }

    [Fact]
    public void Hash_changes_when_content_changes()
    {
        var a = LibraryContent.Hash("alpha");
        var b = LibraryContent.Hash("beta");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => LibraryContent.Hash(null!));
    }
}
