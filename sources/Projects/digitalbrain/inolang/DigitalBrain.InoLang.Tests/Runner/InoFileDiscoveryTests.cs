using DigitalBrain.InoLang.Tests;

namespace DigitalBrain.InoLang.Tests.Runner;

public sealed class InoFileDiscoveryTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ino-discovery-" + Guid.NewGuid().ToString("N"));

    public InoFileDiscoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    void Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void Enumerates_recursive_ino_files()
    {
        Touch("a/x.ino");
        Touch("a/b/y.ino");
        Touch("c/z.ino");

        var found = InoFileDiscovery.Enumerate(_root);

        found.Should().HaveCount(3);
        found.Should().OnlyContain(p => p.EndsWith(".ino", StringComparison.Ordinal));
        found.Should().Contain(p => p.EndsWith(Path.Combine("a", "x.ino"), StringComparison.Ordinal));
        found.Should().Contain(p => p.EndsWith(Path.Combine("a", "b", "y.ino"), StringComparison.Ordinal));
        found.Should().Contain(p => p.EndsWith(Path.Combine("c", "z.ino"), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("Generated")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    public void Skips_excluded_directory(string excluded)
    {
        Touch($"{excluded}/skip.ino");
        Touch("keep.ino");

        var found = InoFileDiscovery.Enumerate(_root);

        found.Should().ContainSingle()
            .Which.Should().EndWith("keep.ino");
    }

    [Fact]
    public void Skips_excluded_directories_at_nested_depth()
    {
        Touch("deep/nested/obj/skip.ino");
        Touch("deep/nested/keep.ino");

        var found = InoFileDiscovery.Enumerate(_root);

        found.Should().ContainSingle()
            .Which.Should().EndWith(Path.Combine("deep", "nested", "keep.ino"));
    }

    [Fact]
    public void Skips_non_ino_extensions()
    {
        Touch("x.cs");
        Touch("y.txt");
        Touch("z.ino");

        InoFileDiscovery.Enumerate(_root).Should().ContainSingle()
            .Which.Should().EndWith("z.ino");
    }

    [Fact]
    public void Returns_empty_for_directory_with_no_ino()
    {
        Touch("a.txt");
        InoFileDiscovery.Enumerate(_root).Should().BeEmpty();
    }

    [Fact]
    public void Ordering_is_deterministic_across_calls()
    {
        Touch("b/two.ino");
        Touch("a/one.ino");
        Touch("c/three.ino");

        var first = InoFileDiscovery.Enumerate(_root);
        var second = InoFileDiscovery.Enumerate(_root);

        second.Should().Equal(first);
    }

    [Fact]
    public void Throws_for_missing_root()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        Action act = () => InoFileDiscovery.Enumerate(missing);
        act.Should().Throw<DirectoryNotFoundException>();
    }
}
