using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Analyzers.Apps;
using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>Compiles synapse source with every shipped DigitalBrain analyzer plus DB0041-DB0045; caches successful builds by hash.</summary>
public sealed class SynapseCompiler
{
    private const int Capacity = 256;
    private const string GlobalUsingsPath = "GlobalUsings.cs";

    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using DigitalBrain;
        global using DigitalBrain.Rules;
        global using DigitalBrain.Time;
        global using DigitalBrain.UI;
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = ImmutableArray.Create<DiagnosticAnalyzer>([.. AnalyzerSet.All, .. AnalyzerSet.Synapses]);

    private readonly ReferenceSet references;
    private readonly ImmutableArray<DiagnosticAnalyzer> analyzers;
    private readonly Lock gate = new();
    private readonly Dictionary<string, LinkedListNode<SynapseBuild>> cache = new(StringComparer.Ordinal);
    private readonly LinkedList<SynapseBuild> recency = new();
    private int emits;

    /// <summary>Creates a compiler over the given references.</summary>
    /// <param name="references">The references the source compiles against.</param>
    public SynapseCompiler(ReferenceSet references)
        : this(references, [])
    {
    }

    // withoutBannedApis drops DB0045 so a test can emit a build that only the host's own banned-API check stands in front of.
    internal SynapseCompiler(ReferenceSet references, IEnumerable<DiagnosticAnalyzer> extraAnalyzers, bool withoutBannedApis = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        this.references = references;
        analyzers = [.. Analyzers.Where(analyzer => !withoutBannedApis || analyzer is not BannedApiAnalyzer), .. extraAnalyzers];
    }

    /// <summary>How many builds this compiler has emitted (cache hits do not count).</summary>
    internal int Emits => Volatile.Read(ref emits);

    /// <summary>Compiles the source, or returns the cached build for the same hash.</summary>
    /// <param name="source">The synapse source.</param>
    /// <returns>The build; <see cref="SynapseBuild.Success"/> is false when any error is reported.</returns>
    public SynapseBuild Compile(SynapseSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var files = source.Files.OrderBy(file => file.Key, StringComparer.Ordinal).ToList();
        var hash = Hash(source.SourceKey, files);
        var assemblyName = $"synapse.{source.SourceKey}.{hash[..8]}";

        lock (gate)
        {
            if (cache.TryGetValue(hash, out var hit))
            {
                recency.Remove(hit);
                recency.AddFirst(hit);
                return hit.Value;
            }
        }

        var trees = files
            .Select(file => CSharpSyntaxTree.ParseText(file.Value, ParseOptions, file.Key, Encoding.UTF8))
            .Append(CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, GlobalUsingsPath, Encoding.UTF8));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            references.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, deterministic: true));

        var analyzed = compilation.WithAnalyzers(analyzers).GetAllDiagnosticsAsync().GetAwaiter().GetResult();
        if (analyzed.Any(IsError))
        {
            return new SynapseBuild(false, Map(analyzed), [], [], hash, assemblyName);
        }

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emitted = compilation.Emit(peStream, pdbStream, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        var diagnostics = analyzed.Concat(emitted.Diagnostics).Distinct().ToList();
        if (!emitted.Success || diagnostics.Any(IsError))
        {
            return new SynapseBuild(false, Map(diagnostics), [], [], hash, assemblyName);
        }

        Interlocked.Increment(ref emits);
        var build = new SynapseBuild(true, Map(diagnostics), peStream.ToArray(), pdbStream.ToArray(), hash, assemblyName);
        lock (gate)
        {
            if (cache.TryGetValue(hash, out var raced))
            {
                return raced.Value;
            }

            cache[hash] = recency.AddFirst(build);
            if (cache.Count > Capacity)
            {
                cache.Remove(recency.Last!.Value.Hash);
                recency.RemoveLast();
            }
        }

        return build;
    }

    private string Hash(string sourceKey, List<KeyValuePair<string, string>> files)
    {
        // The key names the assembly, so it is part of the identity: same files under another key are another build.
        var text = new StringBuilder(references.Fingerprint).Append('\0').Append(sourceKey);
        foreach (var (path, content) in files)
        {
            text.Append('\0').Append(path).Append('\0').Append(content);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // AD0001 is a crashed analyzer: the source was not vetted, so it is refused like an error.
    private static bool IsError(Diagnostic diagnostic) => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Id == "AD0001";

    private static List<SynapseDiagnostic> Map(IEnumerable<Diagnostic> diagnostics) =>
    [
        .. diagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning || diagnostic.Id == "AD0001").Select(diagnostic =>
        {
            var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            if (!diagnostic.Location.IsInSource)
            {
                return new SynapseDiagnostic("", diagnostic.Id, 0, message);
            }

            var span = diagnostic.Location.GetLineSpan();
            return new SynapseDiagnostic(span.Path, diagnostic.Id, span.StartLinePosition.Line + 1, message);
        }),
    ];
}
