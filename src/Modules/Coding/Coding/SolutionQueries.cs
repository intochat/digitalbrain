using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DigitalBrain.Coding;

internal static class SolutionQueries
{
    private const int MaxLimit = 200;

    internal static async Task<SymbolSearchResult> FindSymbolsAsync(Solution solution, SymbolSearch query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Query);
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        var declarations = await SymbolFinder.FindSourceDeclarationsAsync(solution,
            name => name.Contains(query.Query, StringComparison.OrdinalIgnoreCase), SymbolFilter.TypeAndMember, cancellationToken).ConfigureAwait(false);
        var hits = declarations
            .Select(symbol => Hit(solution, symbol))
            .OfType<SymbolHit>()
            .OrderBy(static hit => hit.Kind == "NamedType" ? 0 : 1)
            .ThenBy(static hit => hit.Name, StringComparer.Ordinal)
            .ToArray();
        return new SymbolSearchResult([.. hits.Take(limit)], hits.Length, hits.Length > limit);
    }

    internal static async Task<ReferenceSearchResult> ReferencesAsync(Solution solution, ReferenceSearch query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var referenced = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
        var hits = new List<ReferenceHit>();
        foreach (var location in referenced.SelectMany(static reference => reference.Locations))
        {
            var document = location.Document;
            var span = location.Location.GetLineSpan();
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var line = span.StartLinePosition.Line;
            hits.Add(new ReferenceHit(document.FilePath ?? document.Name, line + 1, document.Project.Name, text.Lines[line].ToString().Trim()));
        }

        hits.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path) is var byPath && byPath != 0 ? byPath : left.Line.CompareTo(right.Line));
        return new ReferenceSearchResult(query.SymbolId, [.. hits.Take(limit)], hits.Count, hits.Count > limit);
    }

    internal static async Task<DiagnosticsResult> DiagnosticsAsync(Solution solution, DiagnosticsQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        IEnumerable<Diagnostic> diagnostics;
        if (!string.IsNullOrWhiteSpace(query.Path))
        {
            var id = solution.GetDocumentIdsWithFilePath(query.Path).FirstOrDefault()
                ?? throw new InvalidOperationException($"No document at '{query.Path}' is in the solution. Use a path from find-symbols.");
            var model = await solution.GetDocument(id)!.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"'{query.Path}' has no semantic model.");
            diagnostics = model.GetDiagnostics(cancellationToken: cancellationToken);
        }
        else
        {
            var projects = string.IsNullOrWhiteSpace(query.Project)
                ? solution.Projects
                : solution.Projects.Where(project => string.Equals(project.Name, query.Project, StringComparison.OrdinalIgnoreCase));
            var collected = new List<Diagnostic>();
            foreach (var project in projects)
            {
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Project '{project.Name}' has no compilation.");
                collected.AddRange(compilation.GetDiagnostics(cancellationToken));
            }

            diagnostics = collected;
        }

        var hits = diagnostics
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning && diagnostic.Location.IsInSource)
            .Select(static diagnostic => new DiagnosticHit(diagnostic.Id, diagnostic.Severity.ToString(), diagnostic.GetMessage(),
                diagnostic.Location.SourceTree!.FilePath, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(static hit => hit.Severity == "Error" ? 0 : 1)
            .ThenBy(static hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(static hit => hit.Line)
            .ToArray();
        return new DiagnosticsResult([.. hits.Take(limit)],
            hits.Count(static hit => hit.Severity == "Error"), hits.Count(static hit => hit.Severity == "Warning"), hits.Length > limit);
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
