using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Brain.Modules.Behaviors;

public sealed record CompileResult(bool Success, string[] Diagnostics);

public static class BehaviorCompiler
{
    public static CompileResult Check(string source, IEnumerable<string> referenceAssemblyPaths)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "BehaviorCheck",
            [syntaxTree],
            ResolveReferences(referenceAssemblyPaths),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        return new CompileResult(errors.Length == 0, errors);
    }

    private static MetadataReference[] ResolveReferences(IEnumerable<string> referenceAssemblyPaths)
    {
        var trustedPlatformAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator) ?? [];

        return trustedPlatformAssemblies
            .Concat(referenceAssemblyPaths)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
