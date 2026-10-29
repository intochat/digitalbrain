using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Analyzers.Apps;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DigitalBrain.Analyzers.CodeFixes;

// The fixes that rewrite to App base members (08-05, R4-07) change types or remove declarations, so each is offered only when the
// fixed project compiles with no more errors than before: a fix that would break the build is not mechanical.
internal static class FixSafety
{
    public const string AppType = "DigitalBrain.App";
    public const string AppKeyType = "DigitalBrain.AppKey";

    public static async Task<Document?> IfCompiles(Document before, Document after, CancellationToken cancellation)
    {
        var old = await Errors(before.Project, cancellation).ConfigureAwait(false);
        var next = await Errors(after.Project, cancellation).ConfigureAwait(false);
        return next <= old ? after : null;
    }

    public static bool DerivesFromApp(INamedTypeSymbol? type)
    {
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == AppType) { return true; }
        }
        return false;
    }

    private static async Task<int> Errors(Project project, CancellationToken cancellation)
    {
        var compilation = await project.GetCompilationAsync(cancellation).ConfigureAwait(false);
        return compilation?.GetDiagnostics(cancellation).Count(d => d.Severity == DiagnosticSeverity.Error) ?? int.MaxValue;
    }
}

/// <summary>
/// DB0023 fix: a key joined from strings (<c>appKey + "/slot/" + i</c>, <c>$"{appKey}/view"</c>, <c>string.Concat(appKey, "/x")</c>) becomes
/// typed key parts (<c>appKey / "slot" / i</c>). A string key is read with <c>AppKey.Parse</c> first. Offered only when the key comes
/// first, the next part starts with '/', and the fixed project still compiles.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(KeyCompositionFix)), Shared]
public sealed class KeyCompositionFix : CodeFixProvider
{
    /// <summary>The equivalence key of the action.</summary>
    public const string Key = "DB0023.TypedKeyParts";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AppRules.StringKey.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) { return; }
        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node is not ExpressionSyntax expression || Rewrite(model, expression, context.CancellationToken) is not { } replacement) { continue; }
            var changed = context.Document.WithSyntaxRoot(root.ReplaceNode(expression, replacement));
            if (await FixSafety.IfCompiles(context.Document, changed, context.CancellationToken).ConfigureAwait(false) is null) { continue; }
            context.RegisterCodeFix(CodeAction.Create("Use typed key parts", ct => Fix(context.Document, expression, ct), Key), diagnostic);
        }
    }

    private static async Task<Document> Fix(Document document, ExpressionSyntax expression, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);
        if (root is null || model is null || Rewrite(model, expression, cancellation) is not { } replacement) { return document; }
        var changed = document.WithSyntaxRoot(root.ReplaceNode(expression, replacement));
        changed = await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: cancellation).ConfigureAwait(false);
        return await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellation).ConfigureAwait(false);
    }

    // A piece of the joined key: literal text, or an expression.
    private readonly struct Piece
    {
        public Piece(string? text, ExpressionSyntax? expression) { Text = text; Expression = expression; }
        public string? Text { get; }
        public ExpressionSyntax? Expression { get; }
    }

    internal static ExpressionSyntax? Rewrite(SemanticModel model, ExpressionSyntax expression, CancellationToken cancellation)
    {
        var pieces = Pieces(model, expression, cancellation);
        if (pieces is null || pieces.Count < 2 || pieces[0].Expression is not { } key || pieces[1].Text is not { } next || next.Length == 0 || next[0] != '/')
        {
            return null;
        }
        var keyType = model.GetTypeInfo(key, cancellation).Type;
        ExpressionSyntax result;
        if (keyType?.ToDisplayString() == FixSafety.AppKeyType)
        {
            result = Parenthesize(key.WithoutTrivia());
        }
        else if (keyType?.SpecialType == SpecialType.System_String)
        {
            result = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        ParseTypeName(FixSafety.AppKeyType).WithAdditionalAnnotations(Simplifier.Annotation), IdentifierName("Parse")))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(key.WithoutTrivia()))));
        }
        else
        {
            return null;
        }

        // Split the rest into segments at every '/' in the literal text.
        var segments = new List<List<Piece>> { new() };
        foreach (var piece in pieces.Skip(1))
        {
            if (piece.Expression is not null) { segments[segments.Count - 1].Add(piece); continue; }
            var chunks = piece.Text!.Split('/');
            for (var i = 0; i < chunks.Length; i++)
            {
                if (i > 0) { segments.Add(new List<Piece>()); }
                if (chunks[i].Length > 0) { segments[segments.Count - 1].Add(new Piece(chunks[i], null)); }
            }
        }
        var parts = segments.Where(s => s.Count > 0).Select(s => Segment(model, s, cancellation)).ToList();
        if (parts.Count == 0) { return null; }
        foreach (var part in parts)
        {
            result = BinaryExpression(SyntaxKind.DivideExpression, result, part);
        }
        return result.WithTriviaFrom(expression).WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static ExpressionSyntax Segment(SemanticModel model, List<Piece> pieces, CancellationToken cancellation)
    {
        if (pieces.Count == 1)
        {
            var only = pieces[0];
            if (only.Text is not null) { return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(only.Text)); }
            var type = model.GetTypeInfo(only.Expression!, cancellation).Type;
            if (type?.SpecialType is SpecialType.System_String or SpecialType.System_Int32 or SpecialType.System_Int64
                or SpecialType.System_Int16 or SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_SByte)
            {
                return Parenthesize(only.Expression!.WithoutTrivia());
            }
        }
        // Several pieces in one part, or a non-text value: an interpolated part ("slot-{i}").
        var contents = new List<InterpolatedStringContentSyntax>();
        foreach (var piece in pieces)
        {
            if (piece.Text is not null)
            {
                var escaped = piece.Text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("{", "{{").Replace("}", "}}");
                contents.Add(InterpolatedStringText(Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, escaped, piece.Text, TriviaList())));
            }
            else
            {
                contents.Add(Interpolation(piece.Expression!.WithoutTrivia()));
            }
        }
        return InterpolatedStringExpression(Token(SyntaxKind.InterpolatedStringStartToken), List(contents));
    }

    private static ExpressionSyntax Parenthesize(ExpressionSyntax expression)
        => expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or LiteralExpressionSyntax
            or ParenthesizedExpressionSyntax or ElementAccessExpressionSyntax or ThisExpressionSyntax
            ? expression
            : ParenthesizedExpression(expression);

    private static List<Piece>? Pieces(SemanticModel model, ExpressionSyntax expression, CancellationToken cancellation)
    {
        var pieces = new List<Piece>();
        switch (expression)
        {
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                Flatten(model, binary, pieces, cancellation);
                return pieces;
            case InterpolatedStringExpressionSyntax interpolated:
                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax text) { pieces.Add(new Piece(text.TextToken.ValueText, null)); }
                    else if (content is InterpolationSyntax { AlignmentClause: null, FormatClause: null } hole) { pieces.Add(new Piece(null, hole.Expression)); }
                    else { return null; }
                }
                return pieces;
            case InvocationExpressionSyntax invocation:
                foreach (var argument in invocation.ArgumentList.Arguments)
                {
                    if (argument.Expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax) { return null; }
                    pieces.Add(Leaf(argument.Expression));
                }
                return pieces;
            default:
                return null;
        }
    }

    private static void Flatten(SemanticModel model, ExpressionSyntax expression, List<Piece> pieces, CancellationToken cancellation)
    {
        var inner = expression is ParenthesizedExpressionSyntax parenthesized ? parenthesized.Expression : expression;
        if (inner is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression)
            && model.GetTypeInfo(binary, cancellation).Type?.SpecialType == SpecialType.System_String)
        {
            Flatten(model, binary.Left, pieces, cancellation);
            Flatten(model, binary.Right, pieces, cancellation);
            return;
        }
        pieces.Add(Leaf(expression));
    }

    private static Piece Leaf(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? new Piece(literal.Token.ValueText, null)
            : new Piece(null, expression);
}

/// <summary>
/// DB0026 fix: delete the re-implemented helper so the App base member is used: a <c>Key(brain)</c> or <c>Present</c> method is removed,
/// a <c>*RequestLedger</c> type is removed and its uses renamed to the base <c>RequestLedger</c>. Offered only when the fixed project
/// still compiles, which keeps the fix mechanical (a helper whose callers need another shape is left to the author).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BaseBoilerplateFix)), Shared]
public sealed class BaseBoilerplateFix : CodeFixProvider
{
    /// <summary>The equivalence key of the action.</summary>
    public const string Key = "DB0026.UseBaseMember";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AppRules.ReimplementedHelper.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) { return; }
        foreach (var diagnostic in context.Diagnostics)
        {
            var member = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<MemberDeclarationSyntax>();
            if (member is not (MethodDeclarationSyntax or BaseTypeDeclarationSyntax)) { continue; }
            var changed = await Remove(context.Document, member, context.CancellationToken).ConfigureAwait(false);
            if (changed is null || await FixSafety.IfCompiles(context.Document, changed, context.CancellationToken).ConfigureAwait(false) is null) { continue; }
            var name = member is MethodDeclarationSyntax method ? method.Identifier.ValueText : ((BaseTypeDeclarationSyntax)member).Identifier.ValueText;
            context.RegisterCodeFix(
                CodeAction.Create($"Use the App base member instead of '{name}'", ct => Fixed(context.Document, member, ct), Key), diagnostic);
        }
    }

    private static async Task<Document> Fixed(Document document, MemberDeclarationSyntax member, CancellationToken cancellation)
        => await Remove(document, member, cancellation).ConfigureAwait(false) ?? document;

    private static async Task<Document?> Remove(Document document, MemberDeclarationSyntax member, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);
        if (root is null || model is null) { return null; }
        var renamed = new List<SyntaxNode>();
        if (member is BaseTypeDeclarationSyntax type && type.Identifier.ValueText != "RequestLedger"
            && model.GetDeclaredSymbol(type, cancellation) is { } symbol)
        {
            renamed.AddRange(root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(name => name.Identifier.ValueText == type.Identifier.ValueText && !type.Span.Contains(name.Span)
                    && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, cancellation).Symbol?.OriginalDefinition, symbol)));
        }
        var tracked = root.TrackNodes(renamed.Append(member));
        foreach (var name in renamed)
        {
            var current = tracked.GetCurrentNode(name)!;
            tracked = tracked.ReplaceNode(current, IdentifierName("RequestLedger").WithTriviaFrom(current));
        }
        tracked = tracked.RemoveNode(tracked.GetCurrentNode(member)!, SyntaxRemoveOptions.KeepNoTrivia)!;
        return document.WithSyntaxRoot(tracked);
    }
}

/// <summary>DB0022 second action: record the error state with the App base <c>RecordError</c>, keeping a trailing <c>return;</c> or <c>continue;</c>.</summary>
internal static class RecordErrorAction
{
    public const string Key = "DB0022.RecordError";

    // Only in an instance member of a class that derives from DigitalBrain.App, where the protected RecordError is reachable.
    public static bool Applies(SemanticModel model, CatchClauseSyntax clause, CancellationToken cancellation)
    {
        var symbol = model.GetEnclosingSymbol(clause.SpanStart, cancellation);
        for (var current = symbol; current is not null && current is not INamedTypeSymbol; current = current.ContainingSymbol)
        {
            if (current.IsStatic) { return false; }
        }
        var owner = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
        return FixSafety.DerivesFromApp(owner);
    }

    public static async Task<Document> Fix(Document document, CatchClauseSyntax clause, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);
        if (root is null || model is null) { return document; }
        var name = clause.Declaration?.Identifier.ValueText;
        if (string.IsNullOrEmpty(name))
        {
            name = "error";
            var taken = new HashSet<string>(model.LookupSymbols(clause.Block.SpanStart).Select(s => s.Name));
            for (var i = 2; taken.Contains(name); i++) { name = "error" + i; }
        }
        var declaration = clause.Declaration is { } existing
            ? existing.WithIdentifier(Identifier(name!)).WithCloseParenToken(existing.CloseParenToken)
            : CatchDeclaration(ParseTypeName("System.Exception").WithAdditionalAnnotations(Simplifier.Annotation), Identifier(name!));
        var record = ExpressionStatement(InvocationExpression(IdentifierName("RecordError"))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName(name!))))));
        var block = clause.Block.WithStatements(clause.Block.Statements.Insert(0, record)).WithAdditionalAnnotations(Formatter.Annotation);
        var next = clause.WithDeclaration(declaration.WithAdditionalAnnotations(Formatter.Annotation)).WithBlock(block);
        if (clause.Declaration is null) { next = next.WithCatchKeyword(clause.CatchKeyword.WithTrailingTrivia(Space)); }
        var changed = document.WithSyntaxRoot(root.ReplaceNode(clause, next));
        changed = await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: cancellation).ConfigureAwait(false);
        return await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellation).ConfigureAwait(false);
    }
}
