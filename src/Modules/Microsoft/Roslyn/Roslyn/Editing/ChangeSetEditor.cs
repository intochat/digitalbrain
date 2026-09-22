using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Microsoft.Roslyn;

public sealed class ChangeSetEditor(CodeFixCatalog codeFixes)
{
    private const int DiagnosticLimit = 200;

    public async Task<EditOutcome> ApplyAsync(Solution original, IReadOnlyList<EditRequest> edits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edits);
        var solution = original;
        var touched = new List<(int Edit, string Path)>();
        for (var index = 0; index < edits.Count; index++)
        {
            var beforeEdit = solution;
            try
            {
                solution = await ApplyOneAsync(solution, edits[index], cancellationToken).ConfigureAwait(false);
                foreach (var path in ChangedPaths(solution, ChangedDocumentIds(beforeEdit, solution)))
                {
                    touched.Add((index, path));
                }
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                var failedIds = ChangedDocumentIds(original, solution);
                return new EditOutcome(solution, [], await DiffAsync(original, solution, failedIds, cancellationToken).ConfigureAwait(false),
                    ChangedPaths(solution, failedIds), index, Describe(index, edits[index], error.Message));
            }
        }

        var changedIds = ChangedDocumentIds(original, solution);
        if (changedIds.Count == 0)
        {
            // Nothing differs from the original snapshot (an empty edit list, or edits that were all no-ops):
            // there is nothing to format, diagnose or attribute an error to.
            return new EditOutcome(solution, [], string.Empty, [], null, null);
        }

        solution = await FormatAsync(solution, changedIds, cancellationToken).ConfigureAwait(false);
        var diagnostics = await DiagnoseAsync(original, solution, changedIds, cancellationToken).ConfigureAwait(false);
        int? failing = null;
        string? detail = null;
        if (diagnostics.Items.FirstOrDefault(static hit => hit.Severity == nameof(DiagnosticSeverity.Error)) is { } firstError)
        {
            // An edit that names the failing file and whose own line range covers the error is the cause
            // even when a later edit touched the same file; otherwise the last edit that touched that file
            // is the likely one, and failing that the last edit overall.
            var inFailingFile = touched.Where(entry => string.Equals(entry.Path, firstError.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
            var responsible = inFailingFile.FirstOrDefault(entry => CoversLine(edits[entry.Edit], firstError.Line));
            responsible = responsible.Path is null ? inFailingFile.LastOrDefault() : responsible;
            failing = responsible.Path is null ? touched[^1].Edit : responsible.Edit;
            detail = Describe(failing.Value, edits[failing.Value], $"left {diagnostics.ErrorCount} error(s); first: {firstError.Id} {firstError.Path}:{firstError.Line} {firstError.Message}");
        }

        return new EditOutcome(solution, diagnostics.Items, await DiffAsync(original, solution, changedIds, cancellationToken).ConfigureAwait(false),
            ChangedPaths(solution, changedIds), failing, detail);
    }

    private async Task<Solution> ApplyOneAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
        => edit.Kind switch
        {
            EditKind.ReplaceMember => await ReplaceMemberAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.InsertMember => await InsertMemberAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.AddUsing => await AddUsingAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.ReplaceRange => await ReplaceRangeAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.Rename => await RenameAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.ApplyCodeFix => await ApplyCodeFixAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown edit kind '{edit.Kind}'."),
        };

    private static async Task<Solution> ReplaceMemberAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var (document, node) = await TargetAsync(solution, edit, cancellationToken).ConfigureAwait(false);
        var replacement = ParseMember(Required(edit.Source, "source"));
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(node, replacement.WithTriviaFrom(node));
        return editor.GetChangedDocument().Project.Solution;
    }

    private static async Task<Solution> InsertMemberAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var (document, node) = await TargetAsync(solution, edit, cancellationToken).ConfigureAwait(false);
        var member = ParseMember(Required(edit.Source, "source"));
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (node is TypeDeclarationSyntax)
        {
            editor.AddMember(node, member);
        }
        else
        {
            editor.InsertAfter(node, member);
        }

        return editor.GetChangedDocument().Project.Solution;
    }

    private static async Task<Solution> AddUsingAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var path = Required(edit.Path, "path");
        var name = Required(edit.Namespace, "namespace");
        var document = SolutionQueries.DocumentAt(solution, path);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException($"'{path}' has no compilation unit.");
        if (root.Usings.Any(directive => directive.Name?.ToString() == name))
        {
            return solution;
        }

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.AddUsings(directive)).Project.Solution;
    }

    private static async Task<Solution> ReplaceRangeAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var path = Required(edit.Path, "path");
        var document = SolutionQueries.DocumentAt(solution, path);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var start = edit.StartLine ?? throw new InvalidOperationException("ReplaceRange needs startLine.");
        var end = edit.EndLine ?? start;
        if (start < 1 || end < start || end > text.Lines.Count)
        {
            throw new InvalidOperationException($"Lines {start}-{end} are outside '{path}' ({text.Lines.Count} lines).");
        }

        var span = TextSpan.FromBounds(text.Lines[start - 1].Start, text.Lines[end - 1].End);
        var changed = text.WithChanges(new TextChange(span, edit.Source ?? string.Empty));
        return document.WithText(changed).Project.Solution;
    }

    private static async Task<Solution> RenameAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var newName = Required(edit.NewName, "newName");
        if (!SyntaxFacts.IsValidIdentifier(newName))
        {
            throw new InvalidOperationException($"'{newName}' is not a valid C# identifier.");
        }

        var symbol = await SolutionQueries.ResolveAsync(solution, Required(edit.SymbolId, "symbolId"), cancellationToken).ConfigureAwait(false);
        var declaringLocation = symbol.Locations.First(static location => location.IsInSource);
        _ = solution.GetDocument(declaringLocation.SourceTree)
            ?? throw new InvalidOperationException($"'{edit.SymbolId}' is declared outside the solution.");
        return await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Solution> ApplyCodeFixAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var path = Required(edit.Path, "path");
        var diagnosticId = Required(edit.DiagnosticId, "diagnosticId");
        var document = SolutionQueries.DocumentAt(solution, path);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"'{path}' has no semantic model.");
        var diagnostic = model.GetDiagnostics(cancellationToken: cancellationToken)
            .FirstOrDefault(candidate => candidate.Id == diagnosticId
                && (edit.StartLine is null || candidate.Location.GetLineSpan().StartLinePosition.Line + 1 == edit.StartLine))
            ?? throw new InvalidOperationException($"No {diagnosticId} diagnostic in '{path}'{(edit.StartLine is { } line ? $" at line {line}" : string.Empty)}. Ask diagnostics for the ids and lines it has.");
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);
        foreach (var provider in codeFixes.For(diagnosticId))
        {
#pragma warning disable CA1031 // a fixer built outside its MEF host may fail in any way; the next fixer gets its turn
            try
            {
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                continue;
            }
#pragma warning restore CA1031
        }

        var flat = actions.SelectMany(static action => action.NestedActions.Length > 0 ? action.NestedActions : [action]).ToArray();
        var chosen = flat.FirstOrDefault(action => edit.FixTitle is null || string.Equals(action.Title, edit.FixTitle, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(flat.Length == 0
                ? $"No code fix is available for {diagnosticId} in this host."
                : $"No fix titled '{edit.FixTitle}'; available: {string.Join(" | ", flat.Select(static action => action.Title))}.");

        Solution changed;
#pragma warning disable CA1031 // running the chosen fix's own operations can fail in any way; surface it as advice through FailingEdit/Detail rather than crash the whole change set
        try
        {
            var operations = await chosen.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            var apply = operations.OfType<ApplyChangesOperation>().FirstOrDefault()
                ?? throw new InvalidOperationException($"The fix '{chosen.Title}' does not change the solution.");
            changed = apply.ChangedSolution;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new InvalidOperationException($"The fix '{chosen.Title}' failed: {error.Message}");
        }
#pragma warning restore CA1031

        return changed;
    }

    private static async Task<(Document Document, SyntaxNode Node)> TargetAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var symbol = await SolutionQueries.ResolveAsync(solution, Required(edit.SymbolId, "symbolId"), cancellationToken).ConfigureAwait(false);
        var node = await SolutionQueries.DeclarationAsync(symbol, cancellationToken).ConfigureAwait(false);
        var document = solution.GetDocument(node.SyntaxTree)
            ?? throw new InvalidOperationException($"'{edit.SymbolId}' is declared outside the solution.");
        return (document, node);
    }

    private static MemberDeclarationSyntax ParseMember(string source)
    {
        var member = SyntaxFactory.ParseMemberDeclaration(source);
        if (member is null || member.ContainsDiagnostics || member.FullSpan.Length < source.Trim().Length)
        {
            throw new InvalidOperationException("source is not one complete member declaration (a method, property, field, event, or type).");
        }

        return member.WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} is required for this edit kind.") : value;

    private static string Describe(int index, EditRequest edit, string message)
        => $"edit {index + 1} ({edit.Kind} {edit.SymbolId ?? edit.Path ?? "?"}) {message}";

    // Only the kinds that name their own lines can be matched against a diagnostic's line; a rename or a
    // member replacement is placed by symbol, so its span comes with the symbol work of a later phase.
    private static bool CoversLine(EditRequest edit, int line)
        => edit.Kind is EditKind.ReplaceRange or EditKind.ApplyCodeFix or EditKind.AddUsing
            && edit.StartLine is { } start
            && line >= start
            && line <= (edit.EndLine ?? start);

    // Every changed document is formatted against the same pre-format snapshot, concurrently, and only the
    // resulting texts are folded back in - the documents do not depend on each other's formatting.
    private static async Task<Solution> FormatAsync(Solution solution, IReadOnlyList<DocumentId> changedIds, CancellationToken cancellationToken)
    {
        var formatted = await Task.WhenAll(changedIds.Select(id =>
            Formatter.FormatAsync(solution.GetDocument(id)!, Formatter.Annotation, cancellationToken: cancellationToken))).ConfigureAwait(false);
        for (var index = 0; index < changedIds.Count; index++)
        {
            var text = await formatted[index].GetTextAsync(cancellationToken).ConfigureAwait(false);
            solution = solution.WithDocumentText(changedIds[index], text);
        }

        return solution;
    }

    // A dependent project can carry diagnostics that predate this change set entirely: another file's unrelated
    // error, or the very same diagnostic merely shifted to a different line by an earlier edit in this file.
    // Matching the pre-edit baseline on (Id, Severity, Message, Path) rather than the full hit (which includes
    // Line) and subtracting it as a multiset - one baseline occurrence cancels one matching after-edit occurrence,
    // in order - keeps a second, genuinely new instance of the same diagnostic from being swallowed by the first.
    private static async Task<DiagnosticsResult> DiagnoseAsync(Solution original, Solution solution, IReadOnlyList<DocumentId> changedIds, CancellationToken cancellationToken)
    {
        var projects = ChangedAndDependents(solution, changedIds);
        // The baseline and the edited tree compile at the same time: both are immutable snapshots and
        // neither result feeds the other, so waiting for the first before starting the second only
        // doubled the wall clock of every check and commit.
        var diagnosed = await Task.WhenAll(
            SolutionQueries.ProjectDiagnosticsAsync(original, projects, DiagnosticLimit, cancellationToken),
            SolutionQueries.ProjectDiagnosticsAsync(solution, projects, DiagnosticLimit, cancellationToken)).ConfigureAwait(false);
        var (before, after) = (diagnosed[0], diagnosed[1]);
        var preexisting = new Dictionary<(string Id, string Severity, string Message, string Path), int>();
        foreach (var hit in before.Items)
        {
            var key = (hit.Id, hit.Severity, hit.Message, hit.Path);
            preexisting[key] = preexisting.GetValueOrDefault(key) + 1;
        }

        var introduced = new List<DiagnosticHit>();
        foreach (var hit in after.Items)
        {
            var key = (hit.Id, hit.Severity, hit.Message, hit.Path);
            if (preexisting.TryGetValue(key, out var count) && count > 0)
            {
                preexisting[key] = count - 1;
            }
            else
            {
                introduced.Add(hit);
            }
        }

        var items = introduced.Take(DiagnosticLimit).ToArray();
        return new DiagnosticsResult(items,
            introduced.Count(static hit => hit.Severity == nameof(DiagnosticSeverity.Error)),
            introduced.Count(static hit => hit.Severity == nameof(DiagnosticSeverity.Warning)),
            introduced.Count > DiagnosticLimit, introduced.Count);
    }

    private static IReadOnlyCollection<ProjectId> ChangedAndDependents(Solution solution, IReadOnlyList<DocumentId> changedIds)
    {
        var graph = solution.GetProjectDependencyGraph();
        var projects = new HashSet<ProjectId>();
        foreach (var projectId in changedIds.Select(static id => id.ProjectId).Distinct())
        {
            projects.Add(projectId);
            projects.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(projectId));
        }

        return projects;
    }

    private static IReadOnlyList<DocumentId> ChangedDocumentIds(Solution original, Solution solution)
        => [.. solution.GetChanges(original).GetProjectChanges().SelectMany(static change => change.GetChangedDocuments())];

    private static IReadOnlyList<string> ChangedPaths(Solution solution, IReadOnlyList<DocumentId> changedIds)
        => [.. changedIds.Select(id => solution.GetDocument(id)!.FilePath ?? id.ToString()).Order(StringComparer.Ordinal)];

    private static async Task<string> DiffAsync(Solution original, Solution solution, IReadOnlyList<DocumentId> changedIds, CancellationToken cancellationToken)
    {
        var diff = new StringBuilder();
        foreach (var id in changedIds)
        {
            var before = await original.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var after = await solution.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            diff.Append(LineDiff.Render(solution.GetDocument(id)!.FilePath ?? id.ToString(), before.ToString(), after.ToString()));
        }

        return diff.ToString();
    }
}