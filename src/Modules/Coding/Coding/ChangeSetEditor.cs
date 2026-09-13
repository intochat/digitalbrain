using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Coding;

public sealed class ChangeSetEditor
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
            try
            {
                var (next, path) = await ApplyOneAsync(solution, edits[index], cancellationToken).ConfigureAwait(false);
                solution = next;
                touched.Add((index, path));
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                return new EditOutcome(solution, [], await DiffAsync(original, solution, cancellationToken).ConfigureAwait(false),
                    ChangedPaths(original, solution), index, Describe(index, edits[index], error.Message));
            }
        }

        solution = await FormatAsync(original, solution, cancellationToken).ConfigureAwait(false);
        var diagnostics = await DiagnoseAsync(original, solution, cancellationToken).ConfigureAwait(false);
        int? failing = null;
        string? detail = null;
        if (diagnostics.Items.FirstOrDefault(static hit => hit.Severity == nameof(DiagnosticSeverity.Error)) is { } firstError)
        {
            // The last edit that touched the failing file is the likely cause; else the last edit overall.
            var responsible = touched.LastOrDefault(entry => string.Equals(entry.Path, firstError.Path, StringComparison.OrdinalIgnoreCase));
            failing = touched.Count == 0 ? 0 : responsible.Path is null ? touched[^1].Edit : responsible.Edit;
            detail = Describe(failing.Value, edits[failing.Value], $"left {diagnostics.ErrorCount} error(s); first: {firstError.Id} {firstError.Path}:{firstError.Line} {firstError.Message}");
        }

        return new EditOutcome(solution, diagnostics.Items, await DiffAsync(original, solution, cancellationToken).ConfigureAwait(false),
            ChangedPaths(original, solution), failing, detail);
    }

    private static async Task<(Solution Solution, string Path)> ApplyOneAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
        => edit.Kind switch
        {
            EditKind.ReplaceMember => await ReplaceMemberAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.InsertMember => await InsertMemberAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.AddUsing => await AddUsingAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.ReplaceRange => await ReplaceRangeAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.Rename => throw new NotSupportedException("phase 1 task 4"),
            EditKind.ApplyCodeFix => throw new NotSupportedException("phase 1 task 4"),
            _ => throw new InvalidOperationException($"Unknown edit kind '{edit.Kind}'."),
        };

    private static async Task<(Solution, string)> ReplaceMemberAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var (document, node) = await TargetAsync(solution, edit, cancellationToken).ConfigureAwait(false);
        var replacement = ParseMember(Required(edit.Source, "source"));
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(node, replacement.WithTriviaFrom(node));
        return (editor.GetChangedDocument().Project.Solution, document.FilePath!);
    }

    private static async Task<(Solution, string)> InsertMemberAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
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

        return (editor.GetChangedDocument().Project.Solution, document.FilePath!);
    }

    private static async Task<(Solution, string)> AddUsingAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var path = Required(edit.Path, "path");
        var name = Required(edit.Namespace, "namespace");
        var document = SolutionQueries.DocumentAt(solution, path);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException($"'{path}' has no compilation unit.");
        if (root.Usings.Any(directive => directive.Name?.ToString() == name))
        {
            return (solution, path);
        }

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);
        return (document.WithSyntaxRoot(root.AddUsings(directive)).Project.Solution, path);
    }

    private static async Task<(Solution, string)> ReplaceRangeAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
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
        return (document.WithText(changed).Project.Solution, path);
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

    private static async Task<Solution> FormatAsync(Solution original, Solution solution, CancellationToken cancellationToken)
    {
        foreach (var id in ChangedDocumentIds(original, solution))
        {
            var formatted = await Formatter.FormatAsync(solution.GetDocument(id)!, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            solution = formatted.Project.Solution;
        }

        return solution;
    }

    // A dependent project can carry diagnostics that predate this change set entirely (another file's unrelated
    // error); diagnosing it against the pre-edit baseline and keeping only what is new attributes errors to the
    // edit that actually caused them, not to whatever else was already broken in the solution.
    private static async Task<DiagnosticsResult> DiagnoseAsync(Solution original, Solution solution, CancellationToken cancellationToken)
    {
        var projects = ChangedAndDependents(original, solution);
        var before = await SolutionQueries.ProjectDiagnosticsAsync(original, projects, DiagnosticLimit, cancellationToken).ConfigureAwait(false);
        var after = await SolutionQueries.ProjectDiagnosticsAsync(solution, projects, DiagnosticLimit, cancellationToken).ConfigureAwait(false);
        var preexisting = new HashSet<DiagnosticHit>(before.Items);
        var introduced = after.Items.Where(hit => !preexisting.Contains(hit)).ToArray();
        return new DiagnosticsResult(introduced,
            introduced.Count(static hit => hit.Severity == nameof(DiagnosticSeverity.Error)),
            introduced.Count(static hit => hit.Severity == nameof(DiagnosticSeverity.Warning)),
            after.Truncated, introduced.Length);
    }

    private static IReadOnlyCollection<ProjectId> ChangedAndDependents(Solution original, Solution solution)
    {
        var graph = solution.GetProjectDependencyGraph();
        var projects = new HashSet<ProjectId>();
        foreach (var change in solution.GetChanges(original).GetProjectChanges())
        {
            projects.Add(change.ProjectId);
            projects.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(change.ProjectId));
        }

        return projects;
    }

    private static IReadOnlyList<DocumentId> ChangedDocumentIds(Solution original, Solution solution)
        => [.. solution.GetChanges(original).GetProjectChanges().SelectMany(static change => change.GetChangedDocuments())];

    private static IReadOnlyList<string> ChangedPaths(Solution original, Solution solution)
        => [.. ChangedDocumentIds(original, solution).Select(id => solution.GetDocument(id)!.FilePath ?? id.ToString()).Order(StringComparer.Ordinal)];

    private static async Task<string> DiffAsync(Solution original, Solution solution, CancellationToken cancellationToken)
    {
        var diff = new StringBuilder();
        foreach (var id in ChangedDocumentIds(original, solution))
        {
            var before = await original.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var after = await solution.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            diff.Append(LineDiff.Render(solution.GetDocument(id)!.FilePath ?? id.ToString(), before.ToString(), after.ToString()));
        }

        return diff.ToString();
    }
}
