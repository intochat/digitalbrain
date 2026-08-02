namespace DigitalBrain.Behaviors.Artifacts;

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Behaviors.Manifest;

internal static class CanonicalArtifactWriter
{
    internal const int MaximumEntries = 128;
    internal const int MaximumEntryBytes = 16 * 1024 * 1024;
    internal const int MaximumExpandedBytes = 64 * 1024 * 1024;
    internal const int MaximumSerializedBytes = MaximumExpandedBytes + (MaximumEntries * 1024 * 1024);

    internal const string ProgramEntryName = "Behavior.cs";
    internal const string FeatureEntryName = "Behavior.feature";

    private static readonly DateTimeOffset CanonicalTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex SecretLikeContent = new(
        """(?i)(password\s*[:=]|api[_-]?key\s*[:=]|secret\s*[:=]|begin\s+(rsa\s+)?private\s+key|begin\s+openssh\s+private\s+key|\bbearer\s+[a-z0-9\-._~+/]+=*)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        ArgumentNullException.ThrowIfNull(envelope.ProgramSource);
        ArgumentNullException.ThrowIfNull(envelope.FeatureSource);

        RejectSecretLikeGeneratedContent(envelope);

        return
        [
            new("manifest.json", CanonicalJson.Serialize(CanonicalizeManifest(envelope.Manifest))),
            new(ProgramEntryName, Encode(envelope.ProgramSource, nameof(envelope.ProgramSource))),
            new(FeatureEntryName, Encode(envelope.FeatureSource, nameof(envelope.FeatureSource))),
            new("dependencies/packages.lock.json", CanonicalJson.Normalize(envelope.PackageLockJson, nameof(envelope.PackageLockJson))),
            new("artifact/Behavior.dll", envelope.BehaviorAssembly),
            new("artifact/Behavior.deps.json", CanonicalJson.Normalize(envelope.BehaviorDependenciesJson, nameof(envelope.BehaviorDependenciesJson))),
            new("evidence/compiler.json", CanonicalJson.Normalize(envelope.CompilerEvidenceJson, nameof(envelope.CompilerEvidenceJson))),
            new("evidence/admission.json", CanonicalJson.Normalize(envelope.AdmissionEvidenceJson, nameof(envelope.AdmissionEvidenceJson))),
            new("evidence/bdd.json", CanonicalJson.Normalize(envelope.BddEvidenceJson, nameof(envelope.BddEvidenceJson))),
        ];
    }

    private static void RejectSecretLikeGeneratedContent(BehaviorArtifactEnvelope envelope)
    {
        Scan("overview", envelope.Manifest.Overview);
        Scan("compiler evidence", envelope.CompilerEvidenceJson);
        Scan("admission evidence", envelope.AdmissionEvidenceJson);
        Scan("bdd evidence", envelope.BddEvidenceJson);
    }

    private static void Scan(string name, string value)
    {
        if (value is not null && SecretLikeContent.IsMatch(value))
        {
            throw new BehaviorArtifactException($"Generated {name} cannot contain secret-like content.");
        }
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

    internal static BehaviorDefinitionManifest CanonicalizeManifest(BehaviorDefinitionManifest manifest)
    {
        ValidateManifest(manifest);

        var contract = CanonicalizeContract(manifest.EntryPoints.Contract);
        return manifest with
        {
            EntryPoints = manifest.EntryPoints with
            {
                EventAliases = manifest.EntryPoints.EventAliases.Order(StringComparer.Ordinal).ToArray(),
                Contract = contract,
            },
            Scenarios = manifest.Scenarios
                .Select(CanonicalizeScenario)
                .OrderBy(scenario => scenario.ScenarioId, StringComparer.Ordinal)
                .ToArray(),
            Overview = manifest.Overview,
            CompilerPolicy = CanonicalizeCompilerPolicy(manifest.CompilerPolicy),
            CapabilityGrants = manifest.CapabilityGrants
                .OrderBy(grant => grant.TargetNeuronContractId, StringComparer.Ordinal)
                .ThenBy(grant => grant.AcceptedRequestSynapseId, StringComparer.Ordinal)
                .ThenBy(grant => grant.AcceptedRequestSchemaVersion)
                .ThenBy(grant => grant.EmittedResultSynapseId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(grant => grant.EmittedResultSchemaVersion ?? 0)
                .ThenBy(grant => grant.TargetInstancePolicy, StringComparer.Ordinal)
                .ThenBy(grant => grant.TargetInstanceName, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static BehaviorContractManifest CanonicalizeContract(BehaviorContractManifest contract)
    {
        if (contract is null
            || string.IsNullOrWhiteSpace(contract.BehaviorContractId)
            || contract.OneOfSchemaJson is null
            || contract.ResultSchemaJson is null
            || contract.Cases is null)
        {
            throw new BehaviorArtifactException("A behavior contract must have an identity, cases, and request/result schemas.");
        }

        if (contract.ContractMajorVersion < 1)
        {
            throw new BehaviorArtifactException("Contract major versions must be positive.");
        }

        var cases = contract.Cases
            .Select(CanonicalizeCase)
            .OrderBy(item => item.CaseId, StringComparer.Ordinal)
            .ThenBy(item => item.CaseSchemaVersion)
            .ToArray();

        if (cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() != cases.Length)
        {
            throw new BehaviorArtifactException("Behavior contract case IDs must be unique.");
        }

        return contract with
        {
            OneOfSchemaJson = CanonicalJson.NormalizeToString(contract.OneOfSchemaJson, nameof(contract.OneOfSchemaJson)),
            ResultSchemaJson = CanonicalJson.NormalizeToString(contract.ResultSchemaJson, nameof(contract.ResultSchemaJson)),
            Cases = cases,
        };
    }

    private static BehaviorContractCaseManifest CanonicalizeCase(BehaviorContractCaseManifest contractCase)
    {
        if (contractCase is null
            || string.IsNullOrWhiteSpace(contractCase.CaseId)
            || string.IsNullOrWhiteSpace(contractCase.CaseName)
            || contractCase.PayloadSchemaJson is null)
        {
            throw new BehaviorArtifactException("Contract cases must have an identifier, name, and payload schema.");
        }

        if (contractCase.CaseSchemaVersion < 1)
        {
            throw new BehaviorArtifactException("Case schema versions must be positive.");
        }

        return contractCase with
        {
            PayloadSchemaJson = CanonicalJson.NormalizeToString(contractCase.PayloadSchemaJson, nameof(contractCase.PayloadSchemaJson)),
        };
    }

    private static BehaviorScenarioManifest CanonicalizeScenario(BehaviorScenarioManifest scenario)
    {
        if (scenario is null
            || string.IsNullOrWhiteSpace(scenario.ScenarioId)
            || string.IsNullOrWhiteSpace(scenario.Title)
            || string.IsNullOrWhiteSpace(scenario.BindingKey))
        {
            throw new BehaviorArtifactException("Scenarios must have a stable identifier, title, and binding key.");
        }

        return scenario;
    }

    private static BehaviorCompilerPolicy CanonicalizeCompilerPolicy(BehaviorCompilerPolicy policy)
    {
        if (policy is null
            || string.IsNullOrWhiteSpace(policy.SdkVersion)
            || string.IsNullOrWhiteSpace(policy.RoslynVersion)
            || string.IsNullOrWhiteSpace(policy.LanguageVersion)
            || string.IsNullOrWhiteSpace(policy.PolicyId))
        {
            throw new BehaviorArtifactException("Compiler policy must record SDK, Roslyn, language version, and policy id.");
        }

        return policy;
    }

    private static void ValidateManifest(BehaviorDefinitionManifest manifest)
    {
        try
        {
            if (manifest is null
                || manifest.EntryPoints is null
                || manifest.EntryPoints.EventAliases is null
                || manifest.EntryPoints.Contract is null
                || manifest.Scenarios is null
                || manifest.Overview is null
                || manifest.CompilerPolicy is null
                || manifest.CapabilityGrants is null
                || manifest.ResourceLimits is null
                || manifest.DisplayName is null
                || manifest.Description is null)
            {
                throw new BehaviorArtifactException("The behavior manifest has missing required members.");
            }

            manifest.Behavior.EnsureValid();
            if (manifest.EntryPoints.EventAliases.Any(string.IsNullOrWhiteSpace)
                || manifest.EntryPoints.EventAliases.Distinct(StringComparer.Ordinal).Count() != manifest.EntryPoints.EventAliases.Count
                || manifest.Scenarios.Select(scenario => scenario.ScenarioId).Distinct(StringComparer.Ordinal).Count() != manifest.Scenarios.Count
                || manifest.CapabilityGrants.Any(grant => !IsDirectedCapabilityGrant(grant))
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

    private static bool IsDirectedCapabilityGrant(BehaviorCapabilityGrant? grant)
    {
        if (grant is null
            || string.IsNullOrWhiteSpace(grant.TargetNeuronContractId)
            || string.IsNullOrWhiteSpace(grant.AcceptedRequestSynapseId)
            || grant.AcceptedRequestSchemaVersion < 1
            || string.IsNullOrWhiteSpace(grant.TargetInstancePolicy)
            || string.IsNullOrWhiteSpace(grant.TargetInstanceName))
        {
            return false;
        }

        if (string.Equals(grant.TargetInstancePolicy, "method-alias", StringComparison.Ordinal)
            || string.Equals(grant.AcceptedRequestSynapseId, "ReadMessage", StringComparison.Ordinal)
            || grant.AcceptedRequestSynapseId.Contains("Method", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasResultId = !string.IsNullOrWhiteSpace(grant.EmittedResultSynapseId);
        var hasResultVersion = grant.EmittedResultSchemaVersion is not null;
        if (hasResultId != hasResultVersion)
        {
            return false;
        }

        if (hasResultVersion && grant.EmittedResultSchemaVersion < 1)
        {
            return false;
        }

        return true;
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
