namespace DigitalBrain.Behaviors.Runtime.Artifacts;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;

public static class CanonicalArtifactReader
{
    private static readonly string[] RequiredEntries =
    [
        "manifest.json",
        "program.cs",
        "dependencies/packages.lock.json",
        "artifact/Behavior.dll",
        "artifact/Behavior.deps.json",
        "evidence/compiler.json",
        "evidence/admission.json",
        "evidence/bdd.json",
    ];

    public static BehaviorArtifactEnvelope Read(ReadOnlyMemory<byte> bytes)
    {
        EnsureNoTrailingBytes(bytes.Span);

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = ValidateEntries(archive);
            var manifest = ReadManifest(entries["manifest.json"]);
            var features = entries
                .Where(entry => entry.Key.StartsWith("features/", StringComparison.Ordinal))
                .ToDictionary(
                    entry => entry.Key["features/".Length..^".feature".Length],
                    entry => ReadText(entry.Value),
                    StringComparer.Ordinal);

            return new BehaviorArtifactEnvelope(
                manifest,
                ReadText(entries["program.cs"]),
                ReadCanonicalJson(entries["dependencies/packages.lock.json"]),
                ReadBytes(entries["artifact/Behavior.dll"]),
                ReadCanonicalJson(entries["artifact/Behavior.deps.json"]),
                features,
                ReadCanonicalJson(entries["evidence/compiler.json"]),
                ReadCanonicalJson(entries["evidence/admission.json"]),
                ReadCanonicalJson(entries["evidence/bdd.json"]));
        }
        catch (BehaviorArtifactException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact is not a valid ZIP envelope.", exception);
        }
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > CanonicalArtifactWriter.MaximumEntries)
        {
            throw new BehaviorArtifactException($"A behavior artifact cannot contain more than {CanonicalArtifactWriter.MaximumEntries} entries.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var caseInsensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            ValidateName(entry);

            if (!caseInsensitiveNames.Add(entry.FullName))
            {
                throw new BehaviorArtifactException("A behavior artifact cannot contain duplicate or case-colliding names.");
            }

            if (entry.Length > CanonicalArtifactWriter.MaximumEntryBytes)
            {
                throw new BehaviorArtifactException($"A behavior artifact entry cannot exceed {CanonicalArtifactWriter.MaximumEntryBytes} bytes.");
            }

            total = checked(total + entry.Length);

            if (total > CanonicalArtifactWriter.MaximumExpandedBytes)
            {
                throw new BehaviorArtifactException($"A behavior artifact cannot expand beyond {CanonicalArtifactWriter.MaximumExpandedBytes} bytes.");
            }

            if (!IsExpectedName(entry.FullName))
            {
                throw new BehaviorArtifactException($"The behavior artifact contains unknown entry '{entry.FullName}'.");
            }

            entries.Add(entry.FullName, entry);
        }

        foreach (var required in RequiredEntries)
        {
            if (!entries.ContainsKey(required))
            {
                throw new BehaviorArtifactException($"The behavior artifact is missing required entry '{required}'.");
            }
        }

        return entries;
    }

    private static void ValidateName(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;

        if (unixFileType == 0xA000)
        {
            throw new BehaviorArtifactException("Behavior artifact entries cannot be symbolic links.");
        }

        if (string.IsNullOrWhiteSpace(name)
            || name.EndsWith('/')
            || name.IndexOfAny(['\\', '\0']) >= 0
            || Path.IsPathRooted(name)
            || name.Split('/').Any(segment => segment is "." or ".." or ""))
        {
            throw new BehaviorArtifactException($"The behavior artifact entry '{name}' is not a safe relative path.");
        }
    }

    private static bool IsExpectedName(string name)
        => RequiredEntries.Contains(name, StringComparer.Ordinal)
            || (name.StartsWith("features/", StringComparison.Ordinal)
                && name.EndsWith(".feature", StringComparison.Ordinal)
                && CanonicalArtifactWriter.IsFeatureName(name["features/".Length..^".feature".Length]));

    private static BehaviorDefinitionManifest ReadManifest(ZipArchiveEntry entry)
    {
        var canonical = ReadCanonicalJson(entry);

        try
        {
            var manifest = JsonSerializer.Deserialize<BehaviorDefinitionManifest>(canonical)
                ?? throw new BehaviorArtifactException("The behavior artifact manifest cannot be null.");
            manifest.Behavior.EnsureValid();
            var canonicalManifest = CanonicalJson.Serialize(CanonicalArtifactWriter.CanonicalizeManifest(manifest));

            if (!canonicalManifest.Span.SequenceEqual(Encoding.UTF8.GetBytes(canonical)))
            {
                throw new BehaviorArtifactException("The behavior artifact manifest is not in canonical order.");
            }

            return CanonicalArtifactWriter.CanonicalizeManifest(manifest);
        }
        catch (JsonException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact manifest does not match the expected contract.", exception);
        }
    }

    private static string ReadCanonicalJson(ZipArchiveEntry entry)
    {
        var text = ReadText(entry);
        var canonical = CanonicalJson.Normalize(text, entry.FullName);
        var normalized = Encoding.UTF8.GetString(canonical.Span);

        if (!string.Equals(text, normalized, StringComparison.Ordinal))
        {
            throw new BehaviorArtifactException($"The behavior artifact entry '{entry.FullName}' is not canonical JSON.");
        }

        return normalized;
    }

    private static string ReadText(ZipArchiveEntry entry)
        => Encoding.UTF8.GetString(ReadBytes(entry));

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream(checked((int)entry.Length));
        input.CopyTo(output);

        if (output.Length != entry.Length)
        {
            throw new BehaviorArtifactException($"The behavior artifact entry '{entry.FullName}' ended before its declared length.");
        }

        return output.ToArray();
    }

    private static void EnsureNoTrailingBytes(ReadOnlySpan<byte> bytes)
    {
        const int endOfCentralDirectoryLength = 22;

        if (bytes.Length < endOfCentralDirectoryLength
            || bytes[^22] != 0x50
            || bytes[^21] != 0x4B
            || bytes[^20] != 0x05
            || bytes[^19] != 0x06
            || bytes[^2] != 0x00
            || bytes[^1] != 0x00)
        {
            throw new BehaviorArtifactException("A behavior artifact must end exactly at the ZIP end-of-central-directory record.");
        }
    }
}
