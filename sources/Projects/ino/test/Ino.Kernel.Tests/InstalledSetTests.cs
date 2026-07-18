using Ino.Aspire.Hosting;
using Ino.Core;
using Xunit;

namespace Ino.Kernel.Tests;

public class InstalledSetTests
{
    [Fact]
    public void Load_returns_empty_when_file_absent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"installed-missing-{Guid.NewGuid()}.json");
        var set = InstalledSet.Load(path);
        Assert.Empty(set);
    }

    [Fact]
    public void RoundTrip_preserves_domain_ids()
    {
        var path = Path.Combine(Path.GetTempPath(), $"installed-roundtrip-{Guid.NewGuid()}.json");
        try
        {
            var original = new HashSet<DomainId> { DomainId.From("a"), DomainId.From("b") };
            InstalledSet.Save(original, path);

            var loaded = InstalledSet.Load(path);
            Assert.Equal(original.OrderBy(x => x.Value), loaded.OrderBy(x => x.Value));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_uses_atomic_temp_file_rename()
    {
        var path = Path.Combine(Path.GetTempPath(), $"installed-atomic-{Guid.NewGuid()}.json");
        try
        {
            InstalledSet.Save(new HashSet<DomainId> { DomainId.From("x") }, path);
            Assert.True(File.Exists(path));
            // temp file must be renamed away
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
