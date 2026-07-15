using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Shared;

internal static class FeatureSourceReference
{
    internal static string Compute(
        string implementationProjectPath,
        string scenarioProjectPath,
        IEnumerable<(string Path, string Content)> files)
    {
        ArgumentNullException.ThrowIfNull(implementationProjectPath);
        ArgumentNullException.ThrowIfNull(scenarioProjectPath);
        ArgumentNullException.ThrowIfNull(files);
        var entries = files.Select(static file => new SourceEntry(
                "files/" + file.Path,
                Encoding.UTF8.GetBytes(file.Content)))
            .Append(new SourceEntry("entries/implementation", Encoding.UTF8.GetBytes(implementationProjectPath)))
            .Append(new SourceEntry("entries/scenarios", Encoding.UTF8.GetBytes(scenarioProjectPath)))
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var entry in entries)
        {
            var path = Encoding.UTF8.GetBytes(entry.Path);
            BinaryPrimitives.WriteInt64BigEndian(length, path.Length);
            hash.AppendData(length);
            hash.AppendData(path);
            BinaryPrimitives.WriteInt64BigEndian(length, entry.Content.Length);
            hash.AppendData(length);
            hash.AppendData(entry.Content);
        }
        return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed record SourceEntry(string Path, byte[] Content);
}
