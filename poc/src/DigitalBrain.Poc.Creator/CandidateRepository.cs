using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Creator;

public sealed class CandidateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<CandidateManifest> WriteEvidenceMirrorAsync(
        CandidateManifest manifest,
        PocDataRoot root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(root);
        var path = EvidencePath(manifest.Id, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Serialize(manifest);
        await WriteNewAsync(path, bytes, cancellationToken);

        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        return manifest with { CandidateMetadataHash = Hash(bytes) };
    }

    public async Task<CandidateManifest> ReadAsync(
        string id,
        PocDataRoot root,
        CancellationToken cancellationToken = default)
    {
        var path = EvidencePath(id, root);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var manifest = JsonSerializer.Deserialize<CandidateManifest>(bytes, JsonOptions) ??
            throw new InvalidDataException("The candidate evidence mirror is empty.");
        return manifest with { CandidateMetadataHash = Hash(bytes) };
    }

    public async Task ReplaceEvidenceMirrorAsync(
        string id,
        PocDataRoot root,
        string contents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);
        await ReplaceAsync(
            EvidencePath(id, root),
            new UTF8Encoding(false).GetBytes(contents),
            cancellationToken);
    }

    public string DirectoryFor(string id, PocDataRoot root)
    {
        ValidateId(id);
        ArgumentNullException.ThrowIfNull(root);
        return Path.Combine(root.CandidateRoot, id);
    }

    public string EvidencePath(string id, PocDataRoot root) =>
        Path.Combine(DirectoryFor(id, root), "candidate.json");

    public void RequireCanonicalContents(string id, PocDataRoot root)
    {
        var directory = DirectoryFor(id, root);
        if (!Directory.Exists(directory))
        {
            throw new InvalidDataException("The candidate directory is missing.");
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).Any() ||
            !files.SequenceEqual(
                ["candidate.json", "elon-chart.cs", "module.dll"],
                StringComparer.Ordinal))
        {
            throw new InvalidDataException("Candidate storage contains files outside its immutable canonical set.");
        }
    }

    public void RemoveIncompleteCandidate(string id, PocDataRoot root)
    {
        var directory = DirectoryFor(id, root);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(directory, recursive: true);
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] Serialize(CandidateManifest manifest) =>
        new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(manifest, JsonOptions) + "\n");

    private static async Task WriteNewAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task ReplaceAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }

        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteNewAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ValidateId(string id)
    {
        if (id.Length != 64 || id.Any(character =>
            character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new FormatException("A candidate ID must be its lowercase SHA-256 source digest.");
        }
    }
}
