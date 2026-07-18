namespace DigitalBrain.SDK.Sqlite.FileSystem;

public interface IFileSystemGateway
{
    bool TryNormalize(string relativePath, out string fullPath);
    Task<(long size, byte[]? content)> ReadAsync(string fullPath, long contentLimitBytes, CancellationToken ct);
    IReadOnlyList<string> EnumerateRelative(string globPattern, int maxCount);
}
