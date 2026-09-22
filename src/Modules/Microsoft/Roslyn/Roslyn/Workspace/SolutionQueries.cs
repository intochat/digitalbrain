using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Microsoft.Roslyn;

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
            .Where(static hit => !GeneratedDocuments.IsGenerated(hit.Path))
            .OrderBy(hit => string.Equals(hit.Name, query.Query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(static hit => hit.Kind == nameof(SymbolKind.NamedType) ? 0 : 1)
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
            hits.Add(new ReferenceHit(document.FilePath ?? document.Name, line + 1, document.Project.Name, text.Lines[line].ToString().Trim(),
                GeneratedDocuments.IsGenerated(document.FilePath)));
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
        if (!string.IsNullOrWhiteSpace(query.Path))
        {
            var path = query.Path;
            var id = solution.GetDocumentIdsWithFilePath(path).FirstOrDefault()
                ?? throw new InvalidOperationException($"No document at '{path}' is in the solution. Use a path from find-symbols.");
            var model = await solution.GetDocument(id)!.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"'{path}' has no semantic model.");
            var diagnostics = model.GetDiagnostics(cancellationToken: cancellationToken).Select(diagnostic => (diagnostic, path));
            return DiagnosticsResultFrom(diagnostics, limit);
        }

        var projectIds = (string.IsNullOrWhiteSpace(query.Project)
                ? solution.Projects
                : solution.Projects.Where(project => string.Equals(project.Name, query.Project, StringComparison.OrdinalIgnoreCase)))
            .Select(static project => project.Id)
            .ToArray();
        return await ProjectDiagnosticsAsync(solution, projectIds, limit, cancellationToken).ConfigureAwait(false);
    }

    // Shared with ChangeSetEditor, which diagnoses only the projects a change set touched (and their dependents)
    // rather than the whole solution.
    // The projects compile concurrently - a Solution snapshot is immutable, so each compilation is
    // independent - and DiagnosticsResultFrom's own sort restores a deterministic order afterwards. The gate
    // bounds how many compilations run at once: an unfiltered call fans out over every project in the
    // solution, and MSBuildWorkspace compilations are heavy enough that doing all of them at once starves the
    // machine instead of finishing sooner.
    internal static async Task<DiagnosticsResult> ProjectDiagnosticsAsync(Solution solution, IReadOnlyCollection<ProjectId> projects, int limit, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Environment.ProcessorCount);
        var perProject = await Task.WhenAll(projects.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var project = solution.GetProject(id) ?? throw new InvalidOperationException($"Project '{id}' is not in the solution.");
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Project '{project.Name}' has no compilation.");
                var fallbackPath = project.FilePath ?? project.Name;
                return compilation.GetDiagnostics(cancellationToken).Select(diagnostic => (Diagnostic: diagnostic, FallbackPath: fallbackPath));
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return DiagnosticsResultFrom(perProject.SelectMany(static diagnostics => diagnostics), limit);
    }

    private static DiagnosticsResult DiagnosticsResultFrom(IEnumerable<(Diagnostic Diagnostic, string FallbackPath)> diagnostics, int limit)
    {
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

    internal static Document DocumentAt(Solution solution, string path)
    {
        var id = solution.GetDocumentIdsWithFilePath(path).FirstOrDefault()
            ?? throw new InvalidOperationException($"No document at '{path}' is in the solution. Use a path from find-symbols.");
        return solution.GetDocument(id)!;
    }

    internal static async Task<Skeleton> SkeletonAsync(Solution solution, SkeletonQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Path);
        var document = DocumentAt(solution, query.Path);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"'{query.Path}' has no syntax tree.");
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"'{query.Path}' has no semantic model.");
        var members = new List<SkeletonMember>();
        foreach (var node in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (node is BaseNamespaceDeclarationSyntax)
            {
                continue;
            }

            foreach (var symbol in DeclaredSymbols(model, node, cancellationToken))
            {
                if (symbol.GetDocumentationCommentId() is { } id)
                {
                    var depth = node.Ancestors().Count(static ancestor => ancestor is TypeDeclarationSyntax);
                    members.Add(new SkeletonMember(id, symbol.Kind.ToString(), Signature(node), node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, depth));
                }
            }
        }

        return new Skeleton(document.FilePath ?? document.Name, document.Project.Name, members);
    }

    internal static async Task<MemberSource> MemberAsync(Solution solution, MemberQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var node = await DeclarationAsync(symbol, cancellationToken).ConfigureAwait(false);
        var span = node.GetLocation().GetLineSpan();
        return new MemberSource(query.SymbolId, span.Path, span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1, node.ToString());
    }

    internal static async Task<CallersResult> CallersAsync(Solution solution, CallersQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var limit = ClampLimit(query.Limit);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var callers = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
        var hits = callers
            .SelectMany(caller => caller.Locations
                .Where(static location => location.IsInSource)
                .Select(location => new CallerHit(caller.CallingSymbol.GetDocumentationCommentId() ?? caller.CallingSymbol.Name,
                    caller.CallingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    location.SourceTree!.FilePath, location.GetLineSpan().StartLinePosition.Line + 1,
                    solution.GetDocument(location.SourceTree)?.Project.Name ?? string.Empty)))
            .OrderBy(static hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(static hit => hit.Line)
            .ToArray();
        return new CallersResult(query.SymbolId, [.. hits.Take(limit)], hits.Length, hits.Length > limit);
    }

    internal static async Task<SymbolSearchResult> ImplementationsAsync(Solution solution, ImplementationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, projects: null, cancellationToken).ConfigureAwait(false);
        return Envelope(solution, implementations, query.Limit);
    }

    internal static async Task<SymbolSearchResult> DerivedAsync(Solution solution, DerivedQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol type)
        {
            throw new InvalidOperationException($"'{query.SymbolId}' is not a type. Derived types are asked of a class or an interface id.");
        }

        IEnumerable<ISymbol> derived = type.TypeKind == TypeKind.Interface
            ? (await SymbolFinder.FindImplementationsAsync(type, solution, projects: null, cancellationToken).ConfigureAwait(false))
                .Concat(await SymbolFinder.FindDerivedInterfacesAsync(type, solution, transitive: true, projects: null, cancellationToken).ConfigureAwait(false))
            : await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: true, projects: null, cancellationToken).ConfigureAwait(false);
        return Envelope(solution, derived, query.Limit);
    }

    private static SymbolSearchResult Envelope(Solution solution, IEnumerable<ISymbol> symbols, int requestedLimit)
    {
        var limit = ClampLimit(requestedLimit);
        var hits = symbols.Select(symbol => Hit(solution, symbol)).OfType<SymbolHit>()
            .DistinctBy(static hit => hit.Id)
            .OrderBy(static hit => hit.Name, StringComparer.Ordinal)
            .ThenBy(static hit => hit.Id, StringComparer.Ordinal)
            .ToArray();
        return new SymbolSearchResult([.. hits.Take(limit)], hits.Length, hits.Length > limit);
    }

    private static IEnumerable<ISymbol> DeclaredSymbols(SemanticModel model, MemberDeclarationSyntax node, CancellationToken cancellationToken)
    {
        // Field and event declarations declare their variables, not themselves.
        if (node is BaseFieldDeclarationSyntax field)
        {
            return field.Declaration.Variables.Select(variable => model.GetDeclaredSymbol(variable, cancellationToken)).OfType<ISymbol>();
        }

        return model.GetDeclaredSymbol(node, cancellationToken) is { } symbol ? [symbol] : [];
    }

    // The declaration up to its body: "public string Greet(string name)", "public class Greeter : IWelcome".
    private static string Signature(MemberDeclarationSyntax node)
    {
        var end = node switch
        {
            TypeDeclarationSyntax type when type.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken) => type.OpenBraceToken.SpanStart,
            BaseMethodDeclarationSyntax method => method.Body?.SpanStart ?? method.ExpressionBody?.SpanStart ?? method.Span.End,
            PropertyDeclarationSyntax property => property.AccessorList?.SpanStart ?? property.ExpressionBody?.SpanStart ?? property.Span.End,
            IndexerDeclarationSyntax indexer => indexer.AccessorList?.SpanStart ?? indexer.ExpressionBody?.SpanStart ?? indexer.Span.End,
            EventDeclarationSyntax @event => @event.AccessorList?.SpanStart ?? @event.Span.End,
            _ => node.Span.End,
        };
        var text = node.SyntaxTree.GetText().ToString(TextSpan.FromBounds(node.SpanStart, end));
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';').Trim();
    }

    internal static async Task<SyntaxNode> DeclarationAsync(ISymbol symbol, CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault(static reference => reference.SyntaxTree.FilePath.Length > 0)
            ?? throw new InvalidOperationException($"'{symbol.GetDocumentationCommentId()}' has no source declaration.");
        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        // A field's declaring node is the variable; the member to show is the whole field declaration.
        return node is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field } ? field : node;
    }

    // "src/Modules/AI/AI/x.csproj" clusters as "Modules/AI"; "src/Modules/DigitalBrain/Kernel/Kernel/x.csproj" as "Modules/DigitalBrain";
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

    internal static async Task<ISymbol> ResolveAsync(Solution solution, string symbolId, CancellationToken cancellationToken)
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