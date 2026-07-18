using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace DigitalBrain.SDK.Sqlite.FileSystem;

public sealed class FileSystemGateway : IFileSystemGateway
{
    static readonly string RepoRoot =
        Path.GetFullPath(Environment.CurrentDirectory);

    public bool TryNormalize(string relativePath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        if (Path.IsPathRooted(relativePath)) return false;
        var candidate = Path.GetFullPath(
            Path.Combine(RepoRoot, relativePath));
        var relative = Path.GetRelativePath(RepoRoot, candidate);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return false;
        fullPath = candidate;
        return true;
    }

    public async Task<(long size, byte[]? content)> ReadAsync(
        string fullPath, long contentLimitBytes, CancellationToken ct)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists) return (-1, null);
        if (info.Length > contentLimitBytes) return (info.Length, null);
        return (info.Length, await File.ReadAllBytesAsync(fullPath, ct));
    }

    public IReadOnlyList<string> EnumerateRelative(string globPattern, int maxCount)
    {
        var matcher = new Matcher();
        matcher.AddInclude(globPattern);
        var result = matcher.Execute(
            new DirectoryInfoWrapper(new DirectoryInfo(RepoRoot)));
        return result.Files.Select(f => f.Path).Take(maxCount).ToList();
    }
}
