using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Coding;

internal static class SolutionQueries
{
    private const int MaxLimit = 200;

    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    internal static async Task<SymbolSearchResult> FindSymbolsAsync(Solution solution, SymbolSearch query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Query);
        var limit = ClampLimit(query.Limit);
        var declarations = await SymbolFinder.FindSourceDeclarationsAsync(solution,
            name => name.Contains(query.Query, StringComparison.OrdinalIgnoreCase), SymbolFilter.TypeAndMember, cancellationToken).ConfigureAwait(false);
        var hits = declarations
            .Select(symbol => Hit(solution, symbol))
            .OfType<SymbolHit>()
            .OrderBy(static hit => hit.Kind == nameof(SymbolKind.NamedType) ? 0 : 1)
            .ThenBy(static hit => hit.Name, StringComparer.Ordinal)
            .ToArray();
        return new SymbolSearchResult([.. hits.Take(limit)], hits.Length, hits.Length > limit);
    }

    internal static async Task<ReferenceSearchResult> ReferencesAsync(Solution solution, ReferenceSearch query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var limit = ClampLimit(query.Limit);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var referenced = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
        var hits = new List<ReferenceHit>();
        var textByDocument = new Dictionary<DocumentId, SourceText>();
        foreach (var location in referenced.SelectMany(static reference => reference.Locations))
        {
            var document = location.Document;
            var span = location.Location.GetLineSpan();
            if (!textByDocument.TryGetValue(document.Id, out var text))
            {
                text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                textByDocument[document.Id] = text;
            }

            var line = span.StartLinePosition.Line;
            hits.Add(new ReferenceHit(document.FilePath ?? document.Name, line + 1, document.Project.Name, text.Lines[line].ToString().Trim()));
        }

        hits.Sort(static (left, right) =>
        {
            var byPath = string.CompareOrdinal(left.Path, right.Path);
            return byPath != 0 ? byPath : left.Line.CompareTo(right.Line);
        });
        return new ReferenceSearchResult(query.SymbolId, [.. hits.Take(limit)], hits.Count, hits.Count > limit);
    }

    internal static async Task<DiagnosticsResult> DiagnosticsAsync(Solution solution, DiagnosticsQuery query, CancellationToken cancellationToken)
    {
        var limit = ClampLimit(query.Limit);
        List<(Diagnostic Diagnostic, string FallbackPath)> diagnostics;
        if (!string.IsNullOrWhiteSpace(query.Path))
        {
            var path = query.Path;
            var id = solution.GetDocumentIdsWithFilePath(path).FirstOrDefault()
                ?? throw new InvalidOperationException($"No document at '{path}' is in the solution. Use a path from find-symbols.");
            var model = await solution.GetDocument(id)!.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"'{path}' has no semantic model.");
            diagnostics = [.. model.GetDiagnostics(cancellationToken: cancellationToken).Select(diagnostic => (diagnostic, path))];
        }
        else
        {
            var projects = string.IsNullOrWhiteSpace(query.Project)
                ? solution.Projects
                : solution.Projects.Where(project => string.Equals(project.Name, query.Project, StringComparison.OrdinalIgnoreCase));
            diagnostics = [];
            foreach (var project in projects)
            {
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Project '{project.Name}' has no compilation.");
                var fallbackPath = project.FilePath ?? project.Name;
                diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken).Select(diagnostic => (diagnostic, fallbackPath)));
            }
        }

        var hits = diagnostics
            .Where(static entry => entry.Diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Select(static entry => DiagnosticHitFrom(entry.Diagnostic, entry.FallbackPath))
            .OrderBy(static hit => hit.Severity == nameof(DiagnosticSeverity.Error) ? 0 : 1)
            .ThenBy(static hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(static hit => hit.Line)
            .ToArray();
        return new DiagnosticsResult([.. hits.Take(limit)],
            hits.Count(static hit => hit.Severity == nameof(DiagnosticSeverity.Error)),
            hits.Count(static hit => hit.Severity == nameof(DiagnosticSeverity.Warning)),
            hits.Length > limit, hits.Length);
    }

    internal static Task<SolutionMap> MapAsync(Solution solution, string solutionPath, MapQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = string.IsNullOrEmpty(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
        var graph = solution.GetProjectDependencyGraph();
        var byId = solution.Projects.ToDictionary(static project => project.Id);
        var nodes = graph.GetTopologicallySortedProjects(cancellationToken)
            .Select(id => byId[id])
            .Select(project => new ProjectNode(project.Name, project.FilePath ?? project.Name, ClusterOf(root, project),
                query.IncludeDocumentCounts ? project.DocumentIds.Count : 0))
            .ToArray();
        var edges = solution.Projects
            .SelectMany(project => project.ProjectReferences
                .Where(reference => byId.ContainsKey(reference.ProjectId))
                .Select(reference => new ProjectEdge(project.Name, byId[reference.ProjectId].Name)))
            .OrderBy(static edge => edge.From, StringComparer.Ordinal)
            .ThenBy(static edge => edge.To, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(new SolutionMap(solutionPath, nodes, edges));
    }

    // "src/Modules/AI/AI/x.csproj" clusters as "Modules/AI"; "src/Kernel/DigitalBrain/x.csproj" as "Kernel";
    // a project outside src clusters by its own folder name.
    private static string ClusterOf(string? root, Project project)
    {
        var path = project.FilePath;
        if (path is null)
        {
            return project.Name;
        }

        var relative = root is null ? path : Path.GetRelativePath(root, path);
        var segments = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments switch
        {
            ["src", "Modules", var module, ..] => $"Modules/{module}",
            ["src", var area, ..] => area,
            [var single] => Path.GetFileNameWithoutExtension(single),
            _ => segments[^2],
        };
    }

    private static SymbolHit? Hit(Solution solution, ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        if (location is null || symbol.GetDocumentationCommentId() is not { } id)
        {
            return null;
        }

        var document = solution.GetDocument(location.SourceTree);
        return new SymbolHit(id, symbol.Kind.ToString(), symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), document?.Project.Name ?? string.Empty,
            location.SourceTree!.FilePath, location.GetLineSpan().StartLinePosition.Line + 1);
    }

    private static DiagnosticHit DiagnosticHitFrom(Diagnostic diagnostic, string fallbackPath)
        => diagnostic.Location.IsInSource
            ? new DiagnosticHit(diagnostic.Id, diagnostic.Severity.ToString(), diagnostic.GetMessage(),
                diagnostic.Location.SourceTree!.FilePath, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)
            : new DiagnosticHit(diagnostic.Id, diagnostic.Severity.ToString(), diagnostic.GetMessage(), fallbackPath, 0);

    private static async Task<ISymbol> ResolveAsync(Solution solution, string symbolId, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is not null && DocumentationCommentId.GetFirstSymbolForDeclarationId(symbolId, compilation) is { } symbol
                && symbol.Locations.Any(static location => location.IsInSource))
            {
                return symbol;
            }
        }

        throw new InvalidOperationException($"No symbol '{symbolId}' is declared in the solution. Use an id returned by find-symbols.");
    }
}
