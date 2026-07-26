namespace DigitalBrain.Behaviors.Runtime.Artifacts;

using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;

public static class CanonicalArtifactWriter
{
    internal const int MaximumEntries = 128;
    internal const int MaximumEntryBytes = 16 * 1024 * 1024;
    internal const int MaximumExpandedBytes = 64 * 1024 * 1024;

    private static readonly DateTimeOffset CanonicalTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static (BehaviorArtifactDigest Digest, byte[] Bytes) Write(BehaviorArtifactEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var entries = CreateEntries(envelope);
        ValidateEntries(entries);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                var zipEntry = archive.CreateEntry(entry.Name, CompressionLevel.NoCompression);
                zipEntry.LastWriteTime = CanonicalTimestamp;

                using var output = zipEntry.Open();
                output.Write(entry.Bytes.Span);
            }
        }

        var bytes = stream.ToArray();
        return (BehaviorArtifactDigest.Compute(bytes), bytes);
    }

    private static List<ArtifactEntry> CreateEntries(BehaviorArtifactEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope.Manifest);
        ArgumentNullException.ThrowIfNull(envelope.Features);

        var entries = new List<ArtifactEntry>
        {
            new("manifest.json", CanonicalJson.Serialize(CanonicalizeManifest(envelope.Manifest))),
            new("program.cs", Encode(envelope.ProgramSource, nameof(envelope.ProgramSource))),
            new("dependencies/packages.lock.json", CanonicalJson.Normalize(envelope.PackageLockJson, nameof(envelope.PackageLockJson))),
            new("artifact/Behavior.dll", envelope.BehaviorAssembly),
            new("artifact/Behavior.deps.json", CanonicalJson.Normalize(envelope.BehaviorDependenciesJson, nameof(envelope.BehaviorDependenciesJson))),
            new("evidence/compiler.json", CanonicalJson.Normalize(envelope.CompilerEvidenceJson, nameof(envelope.CompilerEvidenceJson))),
            new("evidence/admission.json", CanonicalJson.Normalize(envelope.AdmissionEvidenceJson, nameof(envelope.AdmissionEvidenceJson))),
            new("evidence/bdd.json", CanonicalJson.Normalize(envelope.BddEvidenceJson, nameof(envelope.BddEvidenceJson))),
        };

        foreach (var feature in envelope.Features)
        {
            if (!IsFeatureName(feature.Key))
            {
                throw new BehaviorArtifactException("Feature names must be non-empty ordinal file names without path separators.");
            }

            entries.Add(new($"features/{feature.Key}.feature", Encode(feature.Value, $"features[{feature.Key}]")));
        }

        return entries;
    }

    private static ReadOnlyMemory<byte> Encode(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        return Encoding.UTF8.GetBytes(value);
    }

    internal static bool IsFeatureName(string value)
        => !string.IsNullOrEmpty(value)
            && value.IndexOfAny(['/', '\\', '\0']) < 0
            && value is not "." and not "..";

    internal static BehaviorDefinitionManifest CanonicalizeManifest(BehaviorDefinitionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.EntryPoints);
        ArgumentNullException.ThrowIfNull(manifest.EntryPoints.EventAliases);
        ArgumentNullException.ThrowIfNull(manifest.EntryPoints.IntentSchemas);
        ArgumentNullException.ThrowIfNull(manifest.CapabilityGrants);
        ArgumentNullException.ThrowIfNull(manifest.ResourceLimits);

        return manifest with
        {
            EntryPoints = manifest.EntryPoints with
            {
                EventAliases = manifest.EntryPoints.EventAliases.Order(StringComparer.Ordinal).ToArray(),
                IntentSchemas = manifest.EntryPoints.IntentSchemas.Order(StringComparer.Ordinal).ToArray(),
            },
            CapabilityGrants = manifest.CapabilityGrants
                .OrderBy(grant => grant.ContractAlias, StringComparer.Ordinal)
                .ThenBy(grant => grant.MethodAlias, StringComparer.Ordinal)
                .ThenBy(grant => grant.Target, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    internal static void ValidateEntries(IReadOnlyList<ArtifactEntry> entries)
    {
        if (entries.Count > MaximumEntries)
        {
            throw new BehaviorArtifactException($"A behavior artifact cannot contain more than {MaximumEntries} entries.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in entries)
        {
            if (!names.Add(entry.Name))
            {
                throw new BehaviorArtifactException("A behavior artifact cannot contain duplicate or case-colliding names.");
            }

            if (entry.Bytes.Length > MaximumEntryBytes)
            {
                throw new BehaviorArtifactException($"A behavior artifact entry cannot exceed {MaximumEntryBytes} bytes.");
            }

            total = checked(total + entry.Bytes.Length);

            if (total > MaximumExpandedBytes)
            {
                throw new BehaviorArtifactException($"A behavior artifact cannot expand beyond {MaximumExpandedBytes} bytes.");
            }
        }
    }

    internal sealed record ArtifactEntry(string Name, ReadOnlyMemory<byte> Bytes);
}

internal static class CanonicalJson
{
    public static ReadOnlyMemory<byte> Serialize<T>(T value)
        => Normalize(JsonSerializer.Serialize(value));

    public static ReadOnlyMemory<byte> Normalize(string value, string parameterName = "json")
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        try
        {
            using var document = JsonDocument.Parse(value);
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                Write(writer, document.RootElement);
            }

            return buffer.WrittenMemory.ToArray();
        }
        catch (JsonException exception)
        {
            throw new BehaviorArtifactException($"{parameterName} must be valid JSON.", exception);
        }
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Object)
        {
            writer.WriteStartObject();

            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (value.ValueKind is JsonValueKind.Array)
        {
            writer.WriteStartArray();

            foreach (var item in value.EnumerateArray())
            {
                Write(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        value.WriteTo(writer);
    }
}
