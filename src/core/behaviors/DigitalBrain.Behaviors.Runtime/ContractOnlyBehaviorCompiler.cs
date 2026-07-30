using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Behaviors;

internal sealed class ContractOnlyBehaviorCompiler : IBehaviorCompiler
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
        if (!lowering.Succeeded)
        {
            // Root-union lowering is required only when the source declares a union.
            // Programs without a root union still compile for existing rail samples.
            if (!LooksLikeUnionSource(programSource))
            {
                return Succeed(assemblyBytes, contract: null, lowering);
            }

            return Fail(lowering.Diagnostics, contract: null, lowering);
        }

        return Succeed(assemblyBytes, lowering.Contract, lowering);
    }

    private static bool LooksLikeUnionSource(string programSource)
    {
        foreach (var line in programSource.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.Contains(" union ", StringComparison.Ordinal)
                || trimmed.StartsWith("union ", StringComparison.Ordinal)
                || trimmed.Contains("\tunion ", StringComparison.Ordinal))
            {
                return true;
            }

            // `public union Pet(...)`
            var tokens = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(static token => token == "union"))
            {
                return true;
            }
        }

        return false;
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
        BehaviorContractManifest? contract,
        InputContractLoweringResult lowering)
        => new(
            true,
            assemblyBytes,
            string.Empty,
            Evidence(true, "ok", contract, lowering),
            contract,
            BehaviorInputContractCompiler.DefaultPolicy);

    private static BehaviorCompileResult Fail(
        string diagnostics,
        BehaviorContractManifest? contract,
        InputContractLoweringResult? lowering = null)
        => new(
            false,
            ReadOnlyMemory<byte>.Empty,
            diagnostics,
            Evidence(false, diagnostics, contract, lowering),
            contract,
            BehaviorInputContractCompiler.DefaultPolicy);

    private static string Evidence(
        bool succeeded,
        string detail,
        BehaviorContractManifest? contract,
        InputContractLoweringResult? lowering)
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
        Add(Assembly.Load("System.Runtime"));
        Add(Assembly.Load("System.Collections"));
        Add(Assembly.Load("System.Linq"));
        Add(Assembly.Load("System.Private.CoreLib"));
        Add(Assembly.Load("netstandard"));
        Add(Assembly.Load("System.Console"));
        Add(Assembly.Load("System.Threading"));
        Add(Assembly.Load("System.Threading.Tasks"));
        Add(Assembly.Load("System.Text.Json"));

        return [.. references];
    }
}
