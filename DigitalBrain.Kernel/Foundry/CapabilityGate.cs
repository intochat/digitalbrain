using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.Kernel.Foundry;

public static class CapabilityGate
{
    private static readonly string[] AllowedNamespacePrefixes =
    {
        "System.",              // narrowed further below by explicit exclusions
        "DigitalBrain.Core.",
    };

    // Explicit exclusions within the broad "System." allowance above — these remain banned
    // even though they start with "System.".
    private static readonly string[] ExcludedWithinSystem =
    {
        "System.Net.",
        "System.IO.",
        "System.Diagnostics.Process.",
        "System.Reflection.Emit.",
        "System.Runtime.InteropServices.",
        "System.Runtime.Loader.",
    };

    // ISymbol.ToDisplayString() with no arguments defaults to a format that renders special types via
    // their C# keyword alias ("string", "int", "object", ...) instead of their CLR name. That alias never
    // starts with "System.", so every member access on one of those ~15 aliased BCL types (string.Empty,
    // "x".ToUpperInvariant(), ...) would be misjudged as an external, non-allowlisted symbol. This format
    // renders fully-qualified CLR names with no alias substitution and no "global::" prefix.
    private static readonly SymbolDisplayFormat FullyQualifiedNoAliasFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static IReadOnlyList<string> FindViolations(CSharpCompilation compilation)
    {
        var violations = new HashSet<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodes())
            {
                if (node is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax))
                    continue;

                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol is null)
                    continue;

                // A bare namespace qualifier (the "System" segment of "System.Diagnostics.Process.Start",
                // a "using System.Net.Http;" import, or the pack's own "namespace Foo.Bar;" declaration) is
                // never itself capability usage — only a resolved type/member reference is. Without this,
                // FoundryCompilation's implicit-usings prelude (which always brings System.Net.Http and
                // System.IO into scope) would flag every single compiled snippet.
                if (symbol.Kind == SymbolKind.Namespace)
                    continue;

                // Symbols declared inside the pack's own single-file compilation — locals, parameters,
                // fields, and the pack's own types/members — aren't external capability usage. Referencing
                // your own code can't smuggle in a banned API, but symbol.ContainingType still resolves to
                // the pack's own class for these, which would otherwise produce a nonsense fullName (e.g.
                // "TelegramResponderNeuron.chatId") that fails every allowlist entry.
                if (SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly))
                    continue;

                var fullName = symbol.ContainingType is null
                    ? symbol.ToDisplayString(FullyQualifiedNoAliasFormat)
                    : symbol.ContainingType.ToDisplayString(FullyQualifiedNoAliasFormat) + "." + symbol.Name;

                if (ExcludedWithinSystem.Any(excluded => fullName.StartsWith(excluded, StringComparison.Ordinal)))
                {
                    violations.Add(ExcludedWithinSystem.First(excluded => fullName.StartsWith(excluded, StringComparison.Ordinal)));
                    continue;
                }

                if (!AllowedNamespacePrefixes.Any(allowed => fullName.StartsWith(allowed, StringComparison.Ordinal)))
                {
                    violations.Add(fullName);
                }
            }
        }
        return violations.ToList();
    }
}
