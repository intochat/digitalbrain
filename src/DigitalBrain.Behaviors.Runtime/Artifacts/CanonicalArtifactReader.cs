namespace DigitalBrain.Behaviors.Runtime.Artifacts;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;

public static class CanonicalArtifactReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
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
        try
        {
            var layout = CanonicalZip.Validate(bytes.Span);
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = ValidateEntries(archive, layout);
            var manifest = ReadManifest(entries["manifest.json"].Entry, entries["manifest.json"].Layout);
            var features = entries
                .Where(entry => entry.Key.StartsWith("features/", StringComparison.Ordinal))
                .ToDictionary(
                    entry => entry.Key["features/".Length..^".feature".Length],
                    entry => ReadText(entry.Value.Entry, entry.Value.Layout),
                    StringComparer.Ordinal);

            var envelope = new BehaviorArtifactEnvelope(
                manifest,
                ReadText(entries["program.cs"].Entry, entries["program.cs"].Layout),
                ReadCanonicalJson(entries["dependencies/packages.lock.json"].Entry, entries["dependencies/packages.lock.json"].Layout),
                ReadBytes(entries["artifact/Behavior.dll"].Entry, entries["artifact/Behavior.dll"].Layout),
                ReadCanonicalJson(entries["artifact/Behavior.deps.json"].Entry, entries["artifact/Behavior.deps.json"].Layout),
                features,
                ReadCanonicalJson(entries["evidence/compiler.json"].Entry, entries["evidence/compiler.json"].Layout),
                ReadCanonicalJson(entries["evidence/admission.json"].Entry, entries["evidence/admission.json"].Layout),
                ReadCanonicalJson(entries["evidence/bdd.json"].Entry, entries["evidence/bdd.json"].Layout));

            if (!CanonicalArtifactWriter.Write(envelope).Bytes.AsSpan().SequenceEqual(bytes.Span))
            {
                throw new BehaviorArtifactException("The behavior artifact ZIP envelope is not a writer-emitted canonical artifact.");
            }

            return envelope;
        }
        catch (BehaviorArtifactException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact is not a valid ZIP envelope.", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact contains non-UTF-8 text.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact contains invalid manifest or ZIP values.", exception);
        }
        catch (OverflowException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact contains values beyond supported limits.", exception);
        }
        catch (IOException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact could not be read safely.", exception);
        }
    }

    private static Dictionary<string, ArchiveEntry> ValidateEntries(ZipArchive archive, IReadOnlyDictionary<string, CanonicalZip.Entry> layout)
    {
        if (archive.Entries.Count > CanonicalArtifactWriter.MaximumEntries)
        {
            throw new BehaviorArtifactException($"A behavior artifact cannot contain more than {CanonicalArtifactWriter.MaximumEntries} entries.");
        }

        if (archive.Entries.Count != layout.Count)
        {
            throw new BehaviorArtifactException("The ZIP parser did not expose the validated central directory.");
        }

        var entries = new Dictionary<string, ArchiveEntry>(StringComparer.Ordinal);
        var caseInsensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            ValidateName(entry);

            if (!caseInsensitiveNames.Add(entry.FullName))
            {
                throw new BehaviorArtifactException("A behavior artifact cannot contain duplicate or case-colliding names.");
            }

            if (!layout.TryGetValue(entry.FullName, out var raw) || entry.Length != raw.Length || entry.CompressedLength != raw.Length)
            {
                throw new BehaviorArtifactException("The ZIP parser entry differs from the validated raw metadata.");
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

            entries.Add(entry.FullName, new ArchiveEntry(entry, raw));
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

    private static BehaviorDefinitionManifest ReadManifest(ZipArchiveEntry entry, CanonicalZip.Entry layout)
    {
        var canonical = ReadCanonicalJson(entry, layout);

        try
        {
            var manifest = JsonSerializer.Deserialize<BehaviorDefinitionManifest>(canonical)
                ?? throw new BehaviorArtifactException("The behavior artifact manifest cannot be null.");
            manifest.Behavior.EnsureValid();
            var canonicalManifest = CanonicalJson.Serialize(CanonicalArtifactWriter.CanonicalizeManifest(manifest));

            if (!canonicalManifest.Span.SequenceEqual(StrictUtf8.GetBytes(canonical)))
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

    private static string ReadCanonicalJson(ZipArchiveEntry entry, CanonicalZip.Entry layout)
    {
        var text = ReadText(entry, layout);
        var canonical = CanonicalJson.Normalize(text, entry.FullName);
        var normalized = StrictUtf8.GetString(canonical.Span);

        if (!string.Equals(text, normalized, StringComparison.Ordinal))
        {
            throw new BehaviorArtifactException($"The behavior artifact entry '{entry.FullName}' is not canonical JSON.");
        }

        return normalized;
    }

    private static string ReadText(ZipArchiveEntry entry, CanonicalZip.Entry layout)
        => StrictUtf8.GetString(ReadBytes(entry, layout));

    private static byte[] ReadBytes(ZipArchiveEntry entry, CanonicalZip.Entry layout)
    {
        using var input = entry.Open();
        var bytes = new byte[layout.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = input.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new BehaviorArtifactException($"The behavior artifact entry '{entry.FullName}' ended before its declared length.");
            }

            offset += read;
        }

        if (input.ReadByte() != -1)
        {
            throw new BehaviorArtifactException($"The behavior artifact entry '{entry.FullName}' exceeded its declared length.");
        }

        return bytes;
    }

    private sealed record ArchiveEntry(ZipArchiveEntry Entry, CanonicalZip.Entry Layout);
}
