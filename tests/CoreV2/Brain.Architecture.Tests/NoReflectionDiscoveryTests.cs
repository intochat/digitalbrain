using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Brain.Architecture.Tests;

public sealed class NoReflectionDiscoveryTests
{
    [Fact]
    public void CoreV2_runtime_contains_no_reflection_discovery()
    {
        var source = SourceTree.Read("srcv2/CoreV2");
        var findings = ReflectionDiscoveryScanner.FindForbidden(source);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("_ = assembly.GetTypes();", "GetTypes")]
    [InlineData("_ = assembly.GetExportedTypes();", "GetExportedTypes")]
    [InlineData("_ = assembly.DefinedTypes;", "DefinedTypes")]
    [InlineData("_ = assembly.GetCustomAttributes(inherit: false);", "GetCustomAttributes")]
    public void Semantic_scanner_rejects_assembly_type_discovery(string discovery, string memberName)
    {
        var source = $$"""
            using System;

            internal static class DiscoveryFixture
            {
                internal static void Discover()
                {
                    var assembly = typeof(string).Assembly;
                    {{discovery}}
                }
            }
            """;

        var findings = ReflectionDiscoveryScanner.FindForbidden([new SourceFile("Fixture.cs", source)]);

        Assert.Contains(findings, finding => finding.MemberName == memberName);
    }

    [Theory]
    [InlineData("_ = Attribute.GetCustomAttributes(assembly);")]
    [InlineData("_ = CustomAttributeExtensions.GetCustomAttributes(assembly);")]
    public void Semantic_scanner_rejects_static_custom_attribute_discovery(string discovery)
    {
        var source = $$"""
            using System;
            using System.Reflection;

            internal static class StaticDiscoveryFixture
            {
                internal static void Discover()
                {
                    var assembly = typeof(string).Assembly;
                    {{discovery}}
                }
            }
            """;

        var findings = ReflectionDiscoveryScanner.FindForbidden([new SourceFile("Fixture.cs", source)]);

        Assert.Contains(findings, finding => finding.MemberName == "GetCustomAttributes");
    }

    [Fact]
    public void Semantic_scanner_allows_non_discovery_reflection()
    {
        const string source = """
            using System;

            internal static class ReflectionFixture
            {
                internal static void InspectKnownType()
                {
                    _ = typeof(string).GetMethod(nameof(string.ToString), Type.EmptyTypes);
                    _ = Attribute.IsDefined(typeof(string), typeof(ObsoleteAttribute));
                }
            }
            """;

        var findings = ReflectionDiscoveryScanner.FindForbidden([new SourceFile("Fixture.cs", source)]);

        Assert.Empty(findings);
    }
}

internal sealed record SourceFile(string Path, string Text);

internal sealed record ReflectionDiscoveryFinding(string Path, int Line, string MemberName);

internal static class ReflectionDiscoveryScanner
{
    private static readonly HashSet<string> AssemblyDiscoveryMembers =
    [
        "DefinedTypes",
        "ExportedTypes",
        "GetExportedTypes",
        "GetForwardedTypes",
        "GetTypes",
    ];

    private static readonly HashSet<string> AttributeDiscoveryMembers =
    [
        "CustomAttributes",
        "GetCustomAttribute",
        "GetCustomAttributes",
        "GetCustomAttributesData",
    ];

    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences = new(CreatePlatformReferences);

    internal static IReadOnlyList<ReflectionDiscoveryFinding> FindForbidden(IReadOnlyList<SourceFile> sources)
    {
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(
                source.Text,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                source.Path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "Brain.Architecture.ReflectionAnalysis",
            syntaxTrees,
            PlatformReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var findings = new List<ReflectionDiscoveryFinding>();

        foreach (var syntaxTree in syntaxTrees)
        {
            var model = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                AddIfForbidden(
                    findings,
                    syntaxTree,
                    memberAccess.Name.Identifier.ValueText,
                    model.GetTypeInfo(memberAccess.Expression).Type,
                    ResolveMemberSymbol(model, memberAccess),
                    memberAccess.GetLocation());
            }

            foreach (var conditionalAccess in root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>())
            {
                var receiverType = model.GetTypeInfo(conditionalAccess.Expression).Type;
                foreach (var memberBinding in conditionalAccess.WhenNotNull.DescendantNodesAndSelf().OfType<MemberBindingExpressionSyntax>())
                {
                    AddIfForbidden(
                        findings,
                        syntaxTree,
                        memberBinding.Name.Identifier.ValueText,
                        receiverType,
                        ResolveMemberSymbol(model, memberBinding),
                        memberBinding.GetLocation());
                }
            }
        }

        return findings;
    }

    private static void AddIfForbidden(
        ICollection<ReflectionDiscoveryFinding> findings,
        SyntaxTree syntaxTree,
        string memberName,
        ITypeSymbol? receiverType,
        ISymbol? memberSymbol,
        Location location)
    {
        if (!IsForbidden(memberName, receiverType, memberSymbol))
        {
            return;
        }

        var line = location.GetLineSpan().StartLinePosition.Line + 1;
        findings.Add(new ReflectionDiscoveryFinding(syntaxTree.FilePath, line, memberName));
    }

    private static bool IsForbidden(string memberName, ITypeSymbol? receiverType, ISymbol? memberSymbol)
    {
        if (AttributeDiscoveryMembers.Contains(memberName) &&
            memberSymbol is IMethodSymbol { IsStatic: true } method &&
            (IsTypeOrBaseType(method.ContainingType, "System.Attribute") ||
             IsTypeOrBaseType(method.ContainingType, "System.Reflection.CustomAttributeExtensions")))
        {
            return true;
        }

        if (receiverType is null)
        {
            return false;
        }

        if (IsTypeOrBaseType(receiverType, "System.Reflection.Assembly"))
        {
            return AssemblyDiscoveryMembers.Contains(memberName) || AttributeDiscoveryMembers.Contains(memberName);
        }

        if (IsTypeOrBaseType(receiverType, "System.Reflection.MemberInfo"))
        {
            return AttributeDiscoveryMembers.Contains(memberName);
        }

        return IsTypeOrBaseType(receiverType, "System.AppDomain") && memberName == "GetAssemblies";
    }

    private static ISymbol? ResolveMemberSymbol(SemanticModel model, ExpressionSyntax memberAccess)
    {
        var symbolNode = memberAccess.Parent is InvocationExpressionSyntax invocation
            ? invocation
            : memberAccess;
        return model.GetSymbolInfo(symbolNode).Symbol;
    }

    private static bool IsTypeOrBaseType(ITypeSymbol type, string metadataName)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"global::{metadataName}")
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<MetadataReference> CreatePlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not expose trusted platform assemblies.");

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}

internal static class SourceTree
{
    internal static IReadOnlyList<SourceFile> Read(string relativePath)
    {
        var root = RepositoryRoot.Find();
        var sourceRoot = Path.Combine(root, relativePath);
        var files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        return files.Select(path => new SourceFile(path, File.ReadAllText(path))).ToArray();
    }
}
