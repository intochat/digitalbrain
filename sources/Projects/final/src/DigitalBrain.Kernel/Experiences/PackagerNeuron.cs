using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.InoLang.Domain.Ino;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;

namespace DigitalBrain.Kernel;

[GrainType("packager")]
public sealed class PackagerNeuron : Neuron, IPackager
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly HashSet<string> LifecycleSynapses =
    [
        nameof(Activated),
        nameof(Deactivated),
        nameof(SynapseIncoming),
        nameof(SynapseOutgoing),
        nameof(NeuronTelemetry)
    ];

    public Task HandleAsync(PackExperience synapse, CancellationToken cancellationToken) =>
        PackAsync(synapse.ExperienceId, synapse.Description, synapse.Version, null, false, null, cancellationToken);

    public Task<ExperiencePacked> PackContractAsync(string contractId, string? description = null, string version = "0.1.0", ContractDeclaration[]? declarations = null, CancellationToken cancellationToken = default) =>
        PackAsync(contractId, description, version, null, isContractOnly: true, contractHandlers: declarations, cancellationToken);

    public async Task<ExperiencePacked> PackAsync(string experienceId, string? description = null, string version = "0.1.0", string? inoContent = null, bool isContractOnly = false, ContractDeclaration[]? contractHandlers = null, CancellationToken cancellationToken = default)
    {
        var brain = GrainFactory.GetGrain<IDigitalBrain>(this.GetPrimaryKeyString());
        var journal = await brain.GetFullJournalAsync(cancellationToken);

        string resolvedDescription;
        string contentHash;
        string[] manifestFiles;
        string? finalIno = null;
        ContractDeclaration[]? finalContractDecls = null;
        string? finalContractJson = null;
        bool finalHasRules = false;

        if (isContractOnly)
        {
            finalContractDecls = contractHandlers ?? Array.Empty<ContractDeclaration>();
            finalContractJson = JsonSerializer.Serialize(finalContractDecls, Json);
            contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(finalContractJson)));
            resolvedDescription = description ?? "Contract-only bundle (synapse vocabulary + handler declarations; impl supplied locally)";
            manifestFiles = [ExperiencePackageFormat.ManifestEntry, ExperiencePackageFormat.ContractEntry];
        }
        else if (!string.IsNullOrWhiteSpace(inoContent) && (inoContent.Contains("name:") || inoContent.Contains("triggers:") || inoContent.Contains("on ") || inoContent.Contains("emits:") || inoContent.Contains("schemaVersion: \"os-on-yaml/")))
        {
            // LLM or creator authored full .ino or .yaml (os-on-yaml per SPEC). Use as experience payload; parse for HasRules + rule contract decls (same AST path for dual).
            finalIno = inoContent;
            resolvedDescription = description ?? "Authored by LLM from prompt";
            var isYaml = inoContent.Contains("schemaVersion: \"os-on-yaml/", StringComparison.OrdinalIgnoreCase);
            contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(finalIno)));
            manifestFiles = [ExperiencePackageFormat.ManifestEntry, isYaml ? ExperiencePackageFormat.YamlEntry : ExperiencePackageFormat.InoEntry];

            // Parse with appropriate loader (YamlParser maps yaml Schema:Neuron/rules to same InoExperience AST as .ino).
            var ast = isYaml
                ? DigitalBrain.InoLang.Domain.Yaml.YamlParser.Parse(finalIno)
                : DigitalBrain.InoLang.Domain.Ino.InoParser.Parse(finalIno);

            finalHasRules = ast?.Rules?.Length > 0;
            ContractDeclaration[]? ruleDecls = null;
            if (finalHasRules && ast != null)
            {
                var decls = new List<ContractDeclaration>();
                foreach (var r in ast.Rules)
                {
                    decls.Add(new ContractDeclaration("IRuleHostNeuron", r.On, true));
                }
                foreach (var e in ast.Emits)
                {
                    decls.Add(new ContractDeclaration("IRuleHostNeuron", e, false));
                }
                ruleDecls = decls.ToArray();
            }
            finalContractDecls = ruleDecls;
        }
        else
        {
            var triggers = journal
                .GroupBy(s => s.GetType().Name)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Where(t => !LifecycleSynapses.Contains(t))
                .Take(3)
                .ToList();
            if (triggers.Count == 0) triggers.Add(experienceId);
            var finalTriggers = triggers.ToArray();

            resolvedDescription = description ?? $"Packed from live usage on {DateTimeOffset.UtcNow:yyyy-MM-dd}";
            finalIno = new StringBuilder()
                .AppendLine($"name: {experienceId}")
                .AppendLine($"version: {version}")
                .AppendLine($"desc: {resolvedDescription}")
                .AppendLine($"triggers: {string.Join(", ", finalTriggers)}")
                .AppendLine($"observed-synapses: {journal.Count}")
                .ToString();
            contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(finalIno)));
            manifestFiles = [ExperiencePackageFormat.ManifestEntry, ExperiencePackageFormat.InoEntry];
        }

        var brainForId = GrainFactory.GetGrain<IDigitalBrain>(this.GetPrimaryKeyString());
        var idForSig = await brainForId.GetIdentityAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sigData = experienceId + "|" + version + "|" + contentHash + "|" + (idForSig.PublicKeyBase64 ?? "");
        var sig = await brainForId.SignAsync(sigData, cancellationToken);
        InoExperience? inoExp = null;
        if (!string.IsNullOrWhiteSpace(finalIno))
        {
            var isYaml = finalIno.Contains("schemaVersion: \"os-on-yaml/", StringComparison.OrdinalIgnoreCase);
            inoExp = isYaml
                ? DigitalBrain.InoLang.Domain.Yaml.YamlParser.Parse(finalIno)
                : InoParser.Parse(finalIno);
        }
        else if (!string.IsNullOrWhiteSpace(inoContent))
        {
            var isYaml = inoContent.Contains("schemaVersion: \"os-on-yaml/", StringComparison.OrdinalIgnoreCase);
            inoExp = isYaml
                ? DigitalBrain.InoLang.Domain.Yaml.YamlParser.Parse(inoContent)
                : InoParser.Parse(inoContent);
        }
        var manifest = new ExperienceManifest(
            experienceId,
            experienceId,
            version,
            resolvedDescription,
            CurrentWorldId ?? Environment.MachineName,
            now,
            contentHash,
            isContractOnly ? Array.Empty<string>() : new[] { experienceId },
            manifestFiles,
            isContractOnly,
            finalContractDecls,
            finalHasRules,
            idForSig.PublicKeyBase64,
            sig,
            inoExp?.DefaultRegion,
            inoExp?.DefaultPinned ?? false,
            inoExp?.DefaultOrder ?? 0,
            inoExp?.Requires ?? Array.Empty<string>(),
            inoExp?.IsSystem ?? false,
            inoExp?.RequiresGrant ?? Array.Empty<string>());

        var outputDirectory = Path.Combine("pa-files", "packages");
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, $"{Sanitize(experienceId)}-{Sanitize(version)}{ExperiencePackageFormat.Extension}");

        using (var zip = new ZipArchive(File.Create(packagePath), ZipArchiveMode.Create))
        {
            WriteEntry(zip, ExperiencePackageFormat.ManifestEntry, JsonSerializer.Serialize(manifest, Json));
            if (isContractOnly)
            {
                WriteEntry(zip, ExperiencePackageFormat.ContractEntry, finalContractJson!);
            }
            else if (finalIno is not null)
            {
                var isYaml = finalIno.Contains("schemaVersion: \"os-on-yaml/", StringComparison.OrdinalIgnoreCase);
                var entryName = isYaml ? ExperiencePackageFormat.YamlEntry : ExperiencePackageFormat.InoEntry;
                WriteEntry(zip, entryName, finalIno);
            }
        }

        var packed = new ExperiencePacked(manifest, packagePath);
        await Emit(packed);

        // Packed result surface removed (was direct Card); rule in os/packager.ino on: PackExperience produces show card surface.
        // Telemetry kept for journal/audit; clients observe ExperiencePacked or rule UiSurface.

        await Emit(new NeuronTelemetry(Self, "ExperiencePacked", new Dictionary<string, string>
        {
            ["id"] = experienceId,
            ["version"] = version,
            ["path"] = packagePath,
            ["hash"] = contentHash,
            ["authored"] = (!string.IsNullOrWhiteSpace(inoContent) || isContractOnly).ToString(),
            ["isContract"] = isContractOnly.ToString()
        }));

        return packed;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string Sanitize(string value) =>
        new(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
}
