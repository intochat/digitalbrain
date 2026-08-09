using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Charting.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Poc.Runtime;

internal sealed class CandidateAssemblyLoader
{
    private const int MaximumFixtureModules = 16;
    private static readonly IReadOnlyDictionary<string, Type> TrustedOutputContracts =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [ContractAlias.For(typeof(AddChartPoint))] = typeof(AddChartPoint),
        };
    private readonly Func<SynapseEnvelope, CancellationToken, Task>? _afterGeneratedLocalOutboxCommit;
    private readonly Func<PendingOutboxEnvelope, CancellationToken, Task>? _afterCandidateDeliveryCommit;

    public CandidateAssemblyLoader()
    {
    }

    public CandidateAssemblyLoader(
        Func<SynapseEnvelope, CancellationToken, Task>? afterGeneratedLocalOutboxCommit,
        Func<PendingOutboxEnvelope, CancellationToken, Task>? afterCandidateDeliveryCommit)
    {
        _afterGeneratedLocalOutboxCommit = afterGeneratedLocalOutboxCommit;
        _afterCandidateDeliveryCommit = afterCandidateDeliveryCommit;
    }

    public async Task<CandidateRuntimeSet> LoadVerifiedFixturesAsync(
        PocDataRoot root,
        IReadOnlyList<VerifiedCandidateModule> modules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count > MaximumFixtureModules)
        {
            throw new InvalidDataException(
                $"A fixture host can load at most {MaximumFixtureModules} verified modules.");
        }

        var duplicateActiveFamilies = modules
            .GroupBy(module => (module.OwnerId, module.Family))
            .Where(group => group.Count() != 1)
            .Select(group => $"{group.Key.OwnerId}/{group.Key.Family.Value}")
            .ToArray();
        if (duplicateActiveFamilies.Length != 0)
        {
            throw new InvalidDataException(
                "A fixture route table must select exactly one active revision per owner/family: " +
                string.Join(", ", duplicateActiveFamilies));
        }

        var duplicateFamilies = modules
            .GroupBy(module => module.Family)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key.Value)
            .ToArray();
        if (duplicateFamilies.Length != 0)
        {
            throw new InvalidDataException(
                "A fixture module list must use globally unique candidate family IDs: " +
                string.Join(", ", duplicateFamilies));
        }

        return await BuildRuntimeSetAsync(
            root,
            modules,
            quarantine: false,
            cancellationToken,
            _afterGeneratedLocalOutboxCommit,
            _afterCandidateDeliveryCommit);
    }

    public Task<CandidateRuntimeSet> LoadTrustedQuarantineAsync(
        PocDataRoot root,
        IReadOnlyList<VerifiedCandidateModule> modules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count != 1)
        {
            throw new InvalidDataException("A trusted quarantine process loads exactly one candidate.");
        }

        return BuildRuntimeSetAsync(
            root,
            modules,
            quarantine: true,
            cancellationToken,
            _afterGeneratedLocalOutboxCommit,
            _afterCandidateDeliveryCommit);
    }

    public Task<CandidateRuntimeSet> LoadTrustedActiveAsync(
        PocDataRoot root,
        IReadOnlyList<VerifiedCandidateModule> modules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count == 0 || modules.Count > MaximumFixtureModules)
        {
            throw new InvalidDataException(
                $"A trusted active host requires between one and {MaximumFixtureModules} modules.");
        }

        var duplicateSelections = modules
            .GroupBy(module => (module.OwnerId, module.Family))
            .Where(group => group.Count() != 1)
            .ToArray();
        var duplicateAssemblyFamilies = modules
            .GroupBy(module => module.Family)
            .Where(group => group.Count() != 1)
            .ToArray();
        if (duplicateSelections.Length != 0 || duplicateAssemblyFamilies.Length != 0)
        {
            throw new InvalidDataException(
                "A trusted active host selects exactly one globally loadable module per owner/family.");
        }

        return BuildRuntimeSetAsync(
            root,
            modules,
            quarantine: true,
            cancellationToken,
            _afterGeneratedLocalOutboxCommit,
            _afterCandidateDeliveryCommit);
    }

    public void VerifyTrustedActive(
        PocDataRoot root,
        IReadOnlyList<VerifiedCandidateModule> modules)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count == 0 || modules.Count > MaximumFixtureModules ||
            modules.GroupBy(module => (module.OwnerId, module.Family)).Any(group => group.Count() != 1) ||
            modules.GroupBy(module => module.Family).Any(group => group.Count() != 1))
        {
            throw new InvalidDataException("The proposed active module selection is not unique and finite.");
        }

        _ = modules.Select(module => LoadOne(root, module, quarantine: true)).ToArray();
    }

    private static async Task<CandidateRuntimeSet> BuildRuntimeSetAsync(
        PocDataRoot root,
        IReadOnlyList<VerifiedCandidateModule> modules,
        bool quarantine,
        CancellationToken cancellationToken,
        Func<SynapseEnvelope, CancellationToken, Task>? afterGeneratedLocalOutboxCommit,
        Func<PendingOutboxEnvelope, CancellationToken, Task>? afterCandidateDeliveryCommit)
    {
        var loaded = modules.Select(module => LoadOne(root, module, quarantine)).ToArray();
        await new RunStore(root).BindCandidateModuleIdentitiesAsync(
            loaded.Select(candidate => new CandidateModuleBinding(
                candidate.Module.OwnerId,
                candidate.Module.Family.Value,
                candidate.Module.Revision,
                candidate.Identity)),
            cancellationToken);
        var routeBindings = loaded.SelectMany(candidate =>
                candidate.Module.GrantedInputAliases.Select(grant =>
                {
                    var handler = candidate.Catalog.Resolve(grant).Single();
                    return RouteBinding.Candidate(
                    candidate.Module.OwnerId,
                    handler.ContractAlias,
                    candidate.Module.Family,
                    candidate.Module.Revision,
                    candidate.Identity,
                    handler.NeuronType.FullName ?? handler.NeuronType.Name);
                }))
            .ToArray();

        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(Synapse).Assembly);
            foreach (var candidate in loaded)
            {
                builder.AddAssembly(candidate.Assembly);
            }
        });
        var serviceProvider = services.BuildServiceProvider();
        return new CandidateRuntimeSet(
            root,
            loaded,
            new ImmutableRouteTable(routeBindings),
            serviceProvider,
            serviceProvider.GetRequiredService<ObjectSerializer>(),
            afterGeneratedLocalOutboxCommit,
            afterCandidateDeliveryCommit);
    }

    private static LoadedCandidate LoadOne(
        PocDataRoot root,
        VerifiedCandidateModule module,
        bool quarantine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module.OwnerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(module.Revision);
        var assemblyPath = ResolveOwned(root.CandidateRoot, module.AssemblyPath);
        var evidencePath = ResolveOwned(root.CandidateRoot, module.EvidencePath);
        if (!File.Exists(assemblyPath) || !File.Exists(evidencePath))
        {
            throw new FileNotFoundException("Verified fixture assembly or evidence is missing.");
        }

        var evidenceBytes = File.ReadAllBytes(evidencePath);
        byte[] sourceBytes;
        using (var evidence = JsonDocument.Parse(evidenceBytes))
        {
            var document = evidence.RootElement;
            if (quarantine)
            {
                RequireEvidence(document, "sourceHashVerified");
                RequireEvidence(document, "assemblyHashVerified");
            }
            else
            {
                RequireEvidence(document, "fixedHeaderVerified");
            }
            if (document.TryGetProperty("familyId", out var evidenceFamily) &&
                evidenceFamily.GetString() != module.Family.Value)
            {
                throw new InvalidDataException("Candidate evidence family does not match the reserved family.");
            }

            var sourceName = quarantine ? "elon-chart.cs" : "probe-neuron.cs";
            if (document.GetProperty("source").GetString() != sourceName ||
                document.GetProperty("assembly").GetString() != "module.dll")
            {
                throw new InvalidDataException("Candidate evidence does not bind its fixed source and assembly names.");
            }

            var sourcePath = ResolveOwned(
                root.CandidateRoot,
                Path.Combine(Path.GetDirectoryName(evidencePath)!, sourceName));
            sourceBytes = File.ReadAllBytes(sourcePath);
            var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
            var sourceHashProperty = quarantine ? "sourceHash" : "sourceSha256";
            if (document.GetProperty(sourceHashProperty).GetString() != sourceHash)
            {
                throw new InvalidDataException("Candidate source does not match its verified canonical hash.");
            }

            ValidateFixedSource(sourceBytes, module.Family, quarantine);
        }

        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assemblyHash = Convert.ToHexString(SHA256.HashData(assemblyBytes)).ToLowerInvariant();
        if (!assemblyHash.Equals(module.AssemblySha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Candidate assembly does not match the trusted verified-module hash.");
        }

        using (var stream = File.OpenRead(assemblyPath))
        using (var pe = new PEReader(stream))
        {
            if (!pe.HasMetadata)
            {
                throw new InvalidDataException("The verified candidate output is not managed IL.");
            }

            var metadata = pe.GetMetadataReader();
            var hasGeneratedSerializer = metadata.TypeDefinitions
                .Select(handle => metadata.GetTypeDefinition(handle))
                .Any(definition =>
                    metadata.GetString(definition.Namespace)
                        .StartsWith("OrleansCodeGen", StringComparison.Ordinal) &&
                    metadata.GetString(definition.Name)
                        .Contains(
                            quarantine ? "ElonPostMatched" : "ProbeSynapse",
                            StringComparison.Ordinal));
            if (!hasGeneratedSerializer)
            {
                throw new InvalidDataException(
                    "The verified candidate assembly does not contain its build-time generated local-synapse serializer.");
            }
        }

        var expectedAssemblyName = $"DigitalBrain.Poc.Candidate.{module.Family.Value}";
        if (!string.Equals(
            AssemblyName.GetAssemblyName(assemblyPath).Name,
            expectedAssemblyName,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("Candidate assembly identity is not derived from its opaque family.");
        }

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var expectedNamespace = expectedAssemblyName;
        var candidateNeuronTypes = assembly.GetTypes()
            .Where(type => typeof(Neuron).IsAssignableFrom(type) && !type.IsAbstract)
            .ToArray();
        if (candidateNeuronTypes.Length == 0 ||
            candidateNeuronTypes.Any(type => type.Namespace != expectedNamespace))
        {
            throw new InvalidDataException("Candidate neuron namespaces are not derived from the opaque family.");
        }

        var catalog = ExactHandlerCatalog.Create(candidateNeuronTypes);
        var localPrefix = $"db.poc.family.{module.Family.Value}.";
        var candidateSynapses = assembly.GetTypes()
            .Where(type => typeof(Synapse).IsAssignableFrom(type) && type != typeof(Synapse))
            .ToArray();
        if (candidateSynapses.Any(type =>
            !ContractAlias.For(type).StartsWith(localPrefix, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("A candidate-local contract alias is not derived from its opaque family.");
        }

        ValidateElonDurableContract(assembly, module, candidateSynapses);

        foreach (var grant in module.GrantedInputAliases)
        {
            var handlers = catalog.Resolve(grant);
            if (handlers.Count != 1)
            {
                throw new InvalidDataException(
                    $"Trusted input grant '{grant}' must select exactly one entry neuron per active family.");
            }
        }

        var grantedCandidateOutputTypes = module.GrantedOutputAliases.Select(grant =>
        {
            var matches = candidateSynapses
                .Where(type => ContractAlias.For(type) == grant)
                .ToArray();
            return matches.Length == 1
                ? matches[0]
                : throw new InvalidDataException(
                    $"Trusted output grant '{grant}' does not name one candidate-local synapse type.");
        }).ToArray();

        var grantedTrustedOutputTypes = module.GrantedTrustedOutputAliases.Select(grant =>
            TrustedOutputContracts.TryGetValue(grant, out var type)
                ? type
                : throw new InvalidDataException(
                    $"Trusted output grant '{grant}' does not name one host-owned trusted contract."))
            .ToArray();
        if (module.GrantedTrustedOutputAliases.Distinct(StringComparer.Ordinal).Count() !=
            module.GrantedTrustedOutputAliases.Count ||
            module.GrantedTargetScopes.Any(string.IsNullOrWhiteSpace) ||
            module.GrantedTargetScopes.Distinct(StringComparer.Ordinal).Count() !=
            module.GrantedTargetScopes.Count)
        {
            throw new InvalidDataException(
                "Trusted output and target-scope grants must be finite, unique, and nonempty.");
        }

        var grantsChartPoints = grantedTrustedOutputTypes.Contains(typeof(AddChartPoint));
        if (grantsChartPoints != (module.GrantedTargetScopes.Count != 0))
        {
            throw new InvalidDataException(
                "An AddChartPoint grant must be paired with an exact finite target-scope grant.");
        }

        return new LoadedCandidate(
            module,
            CandidateModuleIdentity.FromVerifiedBytes(assemblyBytes, sourceBytes, evidenceBytes),
            assembly,
            catalog,
            grantedCandidateOutputTypes,
            grantedTrustedOutputTypes,
            module.GrantedTargetScopes.ToArray());
    }

    private static void ValidateElonDurableContract(
        Assembly assembly,
        VerifiedCandidateModule module,
        IReadOnlyList<Type> candidateSynapses)
    {
        var matched = candidateSynapses.SingleOrDefault(type => type.Name == "ElonPostMatched");
        if (matched is null)
        {
            return;
        }

        var localPrefix = $"db.poc.family.{module.Family.Value}.";
        var grantedAlias = module.GrantedOutputAliases.SingleOrDefault() ?? string.Empty;
        if (!grantedAlias.StartsWith(localPrefix + "matched.v", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The admitted local-synapse alias is not the approved matched schema.");
        }

        var version = grantedAlias[(localPrefix + "matched.").Length..];
        var state = assembly.GetTypes().SingleOrDefault(type => type.Name == "ElonPostRuleState") ??
            throw new InvalidDataException("The admitted module is missing ElonPostRuleState.");
        if (ContractAlias.For(matched) != grantedAlias ||
            ContractAlias.For(state) != localPrefix + "state." + version ||
            !HasContiguousIds(matched, ["PostId", "OccurredAt", "RuleOrdinal"]) ||
            !HasContiguousIds(state, ["AcceptedCount"]))
        {
            throw new InvalidDataException(
                "The admitted module does not implement the approved durable contract and serializer member IDs.");
        }
    }

    private static bool HasContiguousIds(Type type, IReadOnlyList<string> memberNames)
    {
        var properties = memberNames
            .Select(name => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .ToArray();
        if (properties.Any(property => property is null))
        {
            return false;
        }

        return properties.Select((property, index) => property!.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName == "Orleans.IdAttribute" &&
                attribute.ConstructorArguments.Count == 1 &&
                Convert.ToInt32(attribute.ConstructorArguments[0].Value) == index))
            .All(value => value);
    }

    private static void ValidateFixedSource(
        byte[] sourceBytes,
        CandidateFamilyId family,
        bool quarantine)
    {
        var source = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(sourceBytes);
        var expectedDirectives = new List<string>
        {
            "#:sdk Microsoft.NET.Sdk",
            "#:property TargetFramework=net11.0",
            "#:property OutputType=Library",
            "#:property PublishAot=false",
            "#:property ImplicitUsings=disable",
            $"#:property AssemblyName=DigitalBrain.Poc.Candidate.{family.Value}",
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
        };
        if (quarantine)
        {
            expectedDirectives.Add(
                "#:project ../../../src/DigitalBrain.Poc.Social.Contracts/DigitalBrain.Poc.Social.Contracts.csproj");
            expectedDirectives.Add(
                "#:project ../../../src/DigitalBrain.Poc.Charting.Contracts/DigitalBrain.Poc.Charting.Contracts.csproj");
        }
        var actualDirectives = source.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal))
            .ToArray();
        var exactHeader = string.Join("\n", expectedDirectives) + "\n\n";
        if (!source.StartsWith(exactHeader, StringComparison.Ordinal) ||
            !actualDirectives.SequenceEqual(expectedDirectives, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Candidate source does not preserve the exact family-derived fixed header.");
        }

    }

    private static string ResolveOwned(string candidateRoot, string path)
    {
        var resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateRoot));
        var resolvedPath = Path.GetFullPath(path);
        if (!resolvedPath.StartsWith(
            resolvedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Candidate fixture path is outside its run root: {resolvedPath}");
        }

        return resolvedPath;
    }

    private static void RequireEvidence(JsonElement document, string propertyName)
    {
        if (!document.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.True)
        {
            throw new InvalidDataException($"Candidate evidence does not prove '{propertyName}'.");
        }
    }
}
