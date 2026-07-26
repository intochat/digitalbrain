namespace DigitalBrain.Behaviors.Runtime.Artifacts;

using System.IO.Compression;
using System.Text;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;

public static class CanonicalArtifactWriter
{
    internal const int MaximumEntries = 128;
    internal const int MaximumEntryBytes = 16 * 1024 * 1024;
    internal const int MaximumExpandedBytes = 64 * 1024 * 1024;
    internal const int MaximumSerializedBytes = MaximumExpandedBytes + (MaximumEntries * 1024 * 1024);

    private static readonly DateTimeOffset CanonicalTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

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
        CanonicalZip.NormalizeWriterMetadata(bytes);
        _ = CanonicalZip.Validate(bytes);
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
        try
        {
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new BehaviorArtifactException($"{parameterName} must be valid UTF-8 text.", exception);
        }
    }

    internal static bool IsFeatureName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsAsciiAlphaNumeric(value[0]) || !IsAsciiAlphaNumeric(value[^1]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsAsciiAlphaNumeric(character) && character is not '-' and not '_' and not '.')
            {
                return false;
            }
        }

        var baseNameLength = value.IndexOf('.', StringComparison.Ordinal);
        var baseName = baseNameLength < 0 ? value : value[..baseNameLength];
        return !IsWindowsDeviceName(baseName);
    }

    private static bool IsAsciiAlphaNumeric(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsWindowsDeviceName(string value)
        => value is "con" or "prn" or "aux" or "nul"
            || (value.Length == 4 && value.StartsWith("com", StringComparison.Ordinal) && value[3] is >= '1' and <= '9')
            || (value.Length == 4 && value.StartsWith("lpt", StringComparison.Ordinal) && value[3] is >= '1' and <= '9');

    internal static BehaviorDefinitionManifest CanonicalizeManifest(BehaviorDefinitionManifest manifest)
    {
        ValidateManifest(manifest);

        return manifest with
        {
            EntryPoints = manifest.EntryPoints with
            {
                EventAliases = manifest.EntryPoints.EventAliases.Order(StringComparer.Ordinal).ToArray(),
                IntentSchemas = manifest.EntryPoints.IntentSchemas
                    .Select(schema => CanonicalizeSchema(schema))
                    .OrderBy(schema => schema.SchemaId, StringComparer.Ordinal)
                    .ThenBy(schema => schema.SchemaVersion)
                    .ToArray(),
            },
            CapabilityGrants = manifest.CapabilityGrants
                .OrderBy(grant => grant.ContractAlias, StringComparer.Ordinal)
                .ThenBy(grant => grant.MethodAlias, StringComparer.Ordinal)
                .ThenBy(grant => grant.Target, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static BehaviorIntentSchema CanonicalizeSchema(BehaviorIntentSchema schema)
    {
        if (schema is null || string.IsNullOrWhiteSpace(schema.SchemaId)
            || schema.RequestSchemaJson is null || schema.ResultSchemaJson is null)
        {
            throw new BehaviorArtifactException("Intent schemas must have an identifier and request/result contracts.");
        }

        if (schema.SchemaVersion < 1)
        {
            throw new BehaviorArtifactException("Intent schema versions must be positive.");
        }

        return schema with
        {
            RequestSchemaJson = CanonicalJson.NormalizeToString(schema.RequestSchemaJson, nameof(schema.RequestSchemaJson)),
            ResultSchemaJson = CanonicalJson.NormalizeToString(schema.ResultSchemaJson, nameof(schema.ResultSchemaJson)),
        };
    }

    private static void ValidateManifest(BehaviorDefinitionManifest manifest)
    {
        try
        {
            if (manifest is null || manifest.EntryPoints is null
                || manifest.EntryPoints.EventAliases is null || manifest.EntryPoints.IntentSchemas is null
                || manifest.CapabilityGrants is null || manifest.ResourceLimits is null
                || manifest.DisplayName is null || manifest.Description is null)
            {
                throw new BehaviorArtifactException("The behavior manifest has missing required members.");
            }

            manifest.Behavior.EnsureValid();
            if (manifest.EntryPoints.EventAliases.Any(string.IsNullOrWhiteSpace)
                || manifest.EntryPoints.EventAliases.Distinct(StringComparer.Ordinal).Count() != manifest.EntryPoints.EventAliases.Count
                || manifest.EntryPoints.IntentSchemas.Any(schema => schema is null)
                || manifest.EntryPoints.IntentSchemas.Select(schema => (schema.SchemaId, schema.SchemaVersion)).Distinct().Count() != manifest.EntryPoints.IntentSchemas.Count
                || manifest.CapabilityGrants.Any(grant => grant is null || string.IsNullOrWhiteSpace(grant.ContractAlias) || string.IsNullOrWhiteSpace(grant.MethodAlias) || string.IsNullOrWhiteSpace(grant.Target))
                || manifest.ResourceLimits.CpuMilliseconds <= 0 || manifest.ResourceLimits.MemoryBytes <= 0 || manifest.ResourceLimits.WallClockMilliseconds <= 0)
            {
                throw new BehaviorArtifactException("The behavior manifest contains invalid entry points, grants, or resource limits.");
            }
        }
        catch (BehaviorArtifactException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new BehaviorArtifactException("The behavior manifest contains invalid values.", exception);
        }
        catch (FormatException exception)
        {
            throw new BehaviorArtifactException("The behavior manifest contains invalid values.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new BehaviorArtifactException("The behavior manifest contains invalid values.", exception);
        }
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
