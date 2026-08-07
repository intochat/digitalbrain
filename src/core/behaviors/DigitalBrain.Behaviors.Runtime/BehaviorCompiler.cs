using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed class BehaviorCompiler
{
    private static readonly ImmutableArray<string> ForbiddenTypeNames =
    [
        "System.Net.Http.HttpClient",
        "System.IO.File",
        "System.IO.Directory",
        "System.Diagnostics.Process",
        "System.Reflection.Assembly",
        "Orleans.IGrainFactory",
        "System.IServiceProvider",
    ];

    private readonly ImmutableArray<MetadataReference> _references = BuildReferences();
    private readonly ActiveCapabilityCatalog? _catalog;

    public BehaviorCompiler(ActiveCapabilityCatalog? catalog = null)
    {
        _catalog = catalog;
    }

    public BehaviorCompileResult Compile(string programSource, BehaviorId behavior)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programSource);
        behavior.EnsureValid();

        var tree = CSharpSyntaxTree.ParseText(
            programSource,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "Behavior.cs",
            encoding: Encoding.UTF8);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Behavior",
            syntaxTrees: [tree],
            references: _references,
            options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true,
                    nullableContextOptions: NullableContextOptions.Enable)
                .WithMetadataImportOptions(MetadataImportOptions.All));

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        if (!emit.Success)
        {
            var diagnostics = string.Join(
                Environment.NewLine,
                emit.Diagnostics
                    .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()));
            return Fail(diagnostics, contract: null);
        }

        var assemblyBytes = peStream.ToArray();
        var forbidden = FindForbiddenUsage(compilation);
        if (forbidden is not null)
        {
            return Fail(forbidden, contract: null);
        }

        var lowering = BehaviorInputContractCompiler.Lower(programSource, behavior, _references);
        if (!lowering.Succeeded || lowering.Contract is null || lowering.Contract.Cases.Count == 0)
        {
            var diagnostics = lowering.Succeeded
                ? "A successful compile must produce a non-empty behavior input contract."
                : lowering.Diagnostics;
            return Fail(diagnostics, contract: null, lowering);
        }

        var eventAliases = BehaviorInputContractCompiler.DeriveEventAliases(programSource, _references, _catalog);
        if (!eventAliases.Succeeded)
        {
            return Fail(eventAliases.Diagnostics, lowering.Contract, lowering);
        }

        if (eventAliases.EventAliases.Count > 0 && lowering.Contract.Cases.Count != 1)
        {
            return Fail(
                "A behavior that subscribes to a broadcast fact must lower to exactly one input case.",
                lowering.Contract,
                lowering);
        }

        var emitAliases = BehaviorInputContractCompiler.DeriveBroadcastEmitAliases(programSource, _references, _catalog);
        if (!emitAliases.Succeeded)
        {
            return Fail(emitAliases.Diagnostics, lowering.Contract, lowering);
        }

        var grants = BehaviorInputContractCompiler.DeriveCapabilityGrants(programSource, _references, _catalog);
        if (!grants.Succeeded)
        {
            return Fail(grants.Diagnostics, lowering.Contract, lowering);
        }

        if (grants.Grants.Count > 0)
        {
            if (_catalog is null)
            {
                return Fail(
                    "Active capability catalog is required to admit directed capability grants.",
                    lowering.Contract,
                    lowering);
            }

            var admission = BehaviorContractCompatibility.AdmitCapabilityGrants(grants.Grants, _catalog);
            if (!admission.IsAdmitted)
            {
                return Fail(admission.Detail, lowering.Contract, lowering);
            }

            return Succeed(assemblyBytes, lowering.Contract, lowering, admission.Grants, eventAliases.EventAliases, emitAliases.EventAliases);
        }

        return Succeed(assemblyBytes, lowering.Contract, lowering, grants.Grants, eventAliases.EventAliases, emitAliases.EventAliases);
    }

    private static string? FindForbiddenUsage(CSharpCompilation compilation)
    {
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());
        foreach (var node in compilation.SyntaxTrees.First().GetRoot().DescendantNodes())
        {
            var symbol = model.GetSymbolInfo(node).Symbol ?? model.GetTypeInfo(node).Type;
            var display = symbol?.ToDisplayString();
            if (display is null)
            {
                continue;
            }

            foreach (var forbidden in ForbiddenTypeNames)
            {
                if (display.Contains(forbidden, StringComparison.Ordinal))
                {
                    return $"Forbidden type usage: {forbidden}";
                }
            }
        }

        return null;
    }

    private static BehaviorCompileResult Succeed(
        byte[] assemblyBytes,
        BehaviorContractManifest contract,
        InputContractLoweringResult lowering,
        IReadOnlyList<BehaviorCapabilityGrant> grants,
        IReadOnlyList<string> eventAliases,
        IReadOnlyList<string> broadcastEmitAliases)
        => new(
            true,
            assemblyBytes,
            string.Empty,
            Evidence(true, "ok", contract, lowering, grants, eventAliases, broadcastEmitAliases),
            contract,
            BehaviorInputContractCompiler.DefaultPolicy,
            grants,
            eventAliases,
            broadcastEmitAliases.Count == 0 ? null : broadcastEmitAliases);

    private static BehaviorCompileResult Fail(
        string diagnostics,
        BehaviorContractManifest? contract,
        InputContractLoweringResult? lowering = null)
        => new(
            false,
            ReadOnlyMemory<byte>.Empty,
            diagnostics,
            Evidence(false, diagnostics, contract, lowering, grants: null, eventAliases: null, broadcastEmitAliases: null),
            contract,
            BehaviorInputContractCompiler.DefaultPolicy,
            Array.Empty<BehaviorCapabilityGrant>(),
            Array.Empty<string>(),
            null);

    private static string Evidence(
        bool succeeded,
        string detail,
        BehaviorContractManifest? contract,
        InputContractLoweringResult? lowering,
        IReadOnlyList<BehaviorCapabilityGrant>? grants,
        IReadOnlyList<string>? eventAliases,
        IReadOnlyList<string>? broadcastEmitAliases)
    {
        var policy = BehaviorInputContractCompiler.DefaultPolicy;
        return JsonSerializer.Serialize(new
        {
            succeeded,
            detail,
            compiler = "Microsoft.CodeAnalysis.CSharp",
            policy = policy.PolicyId,
            sdk = policy.SdkVersion,
            roslyn = policy.RoslynVersion,
            languageVersion = policy.LanguageVersion,
            contract = contract is null
                ? null
                : new
                {
                    behaviorContractId = contract.BehaviorContractId,
                    contractMajorVersion = contract.ContractMajorVersion,
                    caseIds = contract.Cases.Select(static item => item.CaseId).ToArray(),
                    caseCount = contract.Cases.Count,
                },
            lowering = lowering is null
                ? null
                : new
                {
                    lowering.Succeeded,
                    unionName = lowering.UnionName,
                    caseIds = lowering.CaseIds,
                    detail = lowering.Diagnostics,
                },
            capabilityGrants = grants is null
                ? Array.Empty<object>()
                : grants.Select(static grant => new
                {
                    targetNeuronContractId = grant.TargetNeuronContractId,
                    acceptedRequestSynapseId = grant.AcceptedRequestSynapseId,
                    acceptedRequestSchemaVersion = grant.AcceptedRequestSchemaVersion,
                    emittedResultSynapseId = grant.EmittedResultSynapseId,
                    emittedResultSchemaVersion = grant.EmittedResultSchemaVersion,
                    targetInstancePolicy = grant.TargetInstancePolicy,
                    targetInstanceName = grant.TargetInstanceName,
                }).ToArray(),
            eventAliases = eventAliases ?? Array.Empty<string>(),
            broadcastEmitAliases = broadcastEmitAliases ?? Array.Empty<string>(),
        });
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(Assembly assembly)
        {
            if (string.IsNullOrWhiteSpace(assembly.Location) || !set.Add(assembly.Location))
            {
                return;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        Add(typeof(object).Assembly);
        Add(typeof(Enumerable).Assembly);
        Add(typeof(INeuron).Assembly);
        Add(typeof(IBehaviorProgram<>).Assembly);
        Add(typeof(BehaviorBrain<>).Assembly);
        Add(typeof(Orleans.AliasAttribute).Assembly);
        Add(typeof(Orleans.IGrain).Assembly);
        Add(typeof(System.ComponentModel.DescriptionAttribute).Assembly);
        Add(typeof(DigitalBrain.Google.IGmail).Assembly);
        Add(typeof(DigitalBrain.Salesforce.ISalesforce).Assembly);
        Add(Assembly.Load("System.Runtime"));
        Add(Assembly.Load("System.Collections"));
        Add(Assembly.Load("System.Linq"));
        Add(Assembly.Load("System.Private.CoreLib"));
        Add(Assembly.Load("netstandard"));
        Add(Assembly.Load("System.Console"));
        Add(Assembly.Load("System.Threading"));
        Add(Assembly.Load("System.Threading.Tasks"));
        Add(Assembly.Load("System.Text.Json"));
        Add(Assembly.Load("System.ComponentModel.Primitives"));

        return [.. references];
    }
}
