using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Microsoft.CSharp;

public sealed record ScriptDiagnostic(string File, string Id, int Line, string Message);

public sealed record ScriptCheckResult(bool Success, IReadOnlyList<ScriptDiagnostic> Errors);

// Compiles file-based scripts in-process against this host's loaded assemblies, so an author
// learns about a type error in one round-trip instead of a sandbox verification run. Each file
// is its own single-file app: `#:` directives are file-based-app metadata the compiler must not see,
// and the implicit console usings plus the script client's namespaces are imported as they are
// in the sandbox.
public sealed class CSharpScriptCheck
{
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using DigitalBrain.Client;
        global using DigitalBrain;
        global using DigitalBrain.Contracts;
        """;

    private static readonly CSharpParseOptions Parse = new(LanguageVersion.Preview);
    private static readonly Lazy<MetadataReference[]> References = new(LoadReferences);

    public ScriptCheckResult Check(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0) { throw new ArgumentException("Pass at least one file to check.", nameof(files)); }
        var errors = new List<ScriptDiagnostic>();
        foreach (var (path, source) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(source, path);
            var trees = new[]
            {
                CSharpSyntaxTree.ParseText(SourceText.From(WithoutFileDirectives(source)), Parse, path),
                CSharpSyntaxTree.ParseText(SourceText.From(GlobalUsings), Parse, "GlobalUsings.cs"),
            };
            var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path), trees, References.Value,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));
            errors.AddRange(compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => new ScriptDiagnostic(path, diagnostic.Id,
                    diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1, diagnostic.GetMessage())));
        }
        return new(errors.Count == 0, errors);
    }

    // "#:project ..." lines select references in the sandbox; here every loaded contract assembly
    // is already referenced, so the lines are blanked to keep the remaining line numbers true.
    private static string WithoutFileDirectives(string source)
        => string.Join('\n', source.Split('\n').Select(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal) ? "" : line));

    private static MetadataReference[] LoadReferences()
        => [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
}
