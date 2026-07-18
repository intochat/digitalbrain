using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.V2.Ino;

public static class InoCompiler
{
    public static InoCompileResult Compile(
        GeneratedInoCapsule capsule,
        IEnumerable<Assembly> referenceAssemblies)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            capsule.Source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: capsule.AssemblyName + ".g.cs");

        var compilation = CSharpCompilation.Create(
            capsule.AssemblyName,
            [syntaxTree],
            References(referenceAssemblies),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error,
                nullableContextOptions: NullableContextOptions.Enable));

        using var pe = new MemoryStream();
        var result = compilation.Emit(pe);
        var diagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.IsWarningAsError)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        return result.Success
            ? new InoCompileResult(true, pe.ToArray(), diagnostics)
            : new InoCompileResult(false, [], diagnostics);
    }

    private static PortableExecutableReference[] References(IEnumerable<Assembly> referenceAssemblies)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }
        }

        foreach (var assembly in referenceAssemblies)
        {
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            {
                paths.Add(assembly.Location);
            }
        }

        return paths.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }
}

public sealed record InoCompileResult(
    bool Success,
    byte[] AssemblyBytes,
    string[] Diagnostics)
{
    public Assembly Load() => Assembly.Load(AssemblyBytes);
}
