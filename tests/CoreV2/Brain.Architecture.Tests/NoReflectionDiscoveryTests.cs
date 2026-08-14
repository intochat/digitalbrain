using Xunit;

namespace Brain.Architecture.Tests;

public sealed class NoReflectionDiscoveryTests
{
    [Fact]
    public void CoreV2_runtime_contains_no_reflection_discovery()
    {
        var source = SourceTree.Read("src/CoreV2");

        Assert.DoesNotContain("Assembly.GetTypes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCustomAttributes", source, StringComparison.Ordinal);
    }
}

internal static class SourceTree
{
    internal static string Read(string relativePath)
    {
        var root = RepositoryRoot.Find();
        var sourceRoot = Path.Combine(root, relativePath);
        var files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }
}
