using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Analyzers.Screens;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DigitalBrain.Analyzers.CodeFixes;

/// <summary>
/// DB0022 fixes: make the swallowing catch rethrow (<c>throw;</c>).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EmptyCatchFix)), Shared]
public sealed class EmptyCatchFix : CodeFixProvider
{
    /// <summary>The equivalence key of the rethrow action.</summary>
    public const string Key = "DB0022.Rethrow";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ScreenRules.SwallowingCatch.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<CatchClauseSyntax>() is not { } clause) { continue; }
            context.RegisterCodeFix(CodeAction.Create("Rethrow", ct => Fix(context.Document, clause, ct), Key), diagnostic);
        }
    }

    private static async Task<Document> Fix(Document document, CatchClauseSyntax clause, CancellationToken cancellation)
    {
        var block = clause.Block.WithStatements(SingletonList<StatementSyntax>(ThrowStatement()))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        var changed = document.WithSyntaxRoot(root!.ReplaceNode(clause.Block, block));
        return await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellation).ConfigureAwait(false);
    }
}

/// <summary>
/// DB0025 fix: for <c>JsonSerializer.Serialize*(new { ... })</c>, declare a nested record with the anonymous members
/// (<c>private sealed record &lt;Method&gt;Payload(T A, ...)</c>) and pass <c>new &lt;Method&gt;Payload(...)</c>.
/// Other DB0025 shapes (JsonNode, JsonDocument, string-built JSON) have no mechanical fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TypedRecordFix)), Shared]
public sealed class TypedRecordFix : CodeFixProvider
{
    /// <summary>The equivalence key of the action.</summary>
    public const string Key = "DB0025.TypedRecord";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ScreenRules.HandWrittenJson.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var invocation = node?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation?.ArgumentList.Arguments.FirstOrDefault()?.Expression is not AnonymousObjectCreationExpressionSyntax anonymous) { continue; }
            if (node?.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } owner) { continue; }
            context.RegisterCodeFix(
                CodeAction.Create("Declare a nested record for the payload", ct => Fix(context.Document, owner, anonymous, invocation, ct), Key),
                diagnostic);
        }
    }

    private static async Task<Document> Fix(Document document, TypeDeclarationSyntax owner,
        AnonymousObjectCreationExpressionSyntax anonymous, InvocationExpressionSyntax invocation, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);
        if (root is null || model is null) { return document; }

        var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>()?.Identifier.ValueText ?? "Action";
        var recordName = method + "Payload";

        var parameters = new List<ParameterSyntax>();
        var arguments = new List<ArgumentSyntax>();
        foreach (var declarator in anonymous.Initializers)
        {
            var propertyName = declarator.NameEquals?.Name.Identifier.ValueText
                ?? (declarator.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText
                ?? (declarator.Expression as IdentifierNameSyntax)?.Identifier.ValueText
                ?? "Value";
            var typeInfo = model.GetTypeInfo(declarator.Expression, cancellation);
            var typeName = typeInfo.Type?.ToMinimalDisplayString(model, declarator.SpanStart) ?? "object";
            parameters.Add(Parameter(Identifier(propertyName)).WithType(ParseTypeName(typeName)));
            arguments.Add(Argument(declarator.Expression.WithoutTrivia()));
        }

        var record = RecordDeclaration(Token(SyntaxKind.RecordKeyword), Identifier(recordName))
            .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.SealedKeyword)))
            .WithParameterList(ParameterList(SeparatedList(parameters)))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var creation = ObjectCreationExpression(IdentifierName(recordName))
            .WithArgumentList(ArgumentList(SeparatedList(arguments)))
            .WithTriviaFrom(anonymous);

        var next = owner.ReplaceNode(anonymous, creation);
        next = next.AddMembers(record);
        var changed = document.WithSyntaxRoot(root.ReplaceNode(owner, next));
        return await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellation).ConfigureAwait(false);
    }
}

/// <summary>DB0027 fix: insert a spec comment stub (<c>Role: Optional</c>, <c>Shipped: false</c>, the <c>[On]</c> operations) at the top of the file.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SpecCommentFix)), Shared]
public sealed class SpecCommentFix : CodeFixProvider
{
    /// <summary>The equivalence key of the action.</summary>
    public const string Key = "DB0027.InsertSpecComment";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ScreenRules.MissingSpec.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type) { continue; }
            context.RegisterCodeFix(CodeAction.Create("Insert a spec comment", ct => Fix(context.Document, type, ct), Key), diagnostic);
        }
    }

    /// <summary>The stub text for an app named <paramref name="name"/> handling <paramref name="operations"/>.</summary>
    public static string Stub(string name, IEnumerable<string> operations)
    {
        var list = string.Join(", ", operations);
        return "/*\n# " + name + "\n\nRole: Optional\nShipped: false\n" + (list.Length > 0 ? "Operations: " + list + "\n" : string.Empty)
            + "\nDescribe what " + name + " does and add one \"## Scenario:\" section per behaviour.\n*/\n";
    }

    private static async Task<Document> Fix(Document document, TypeDeclarationSyntax type, CancellationToken cancellation)
    {
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);
        if (root is null) { return document; }
        var operations = new List<string>();
        if (model?.GetDeclaredSymbol(type, cancellation) is { } symbol)
        {
            foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DigitalBrain.OnAttribute"))
                {
                    operations.Add(DigitalBrain.Screens.SpecComment.NormalizeOperation(method.Name));
                }
            }
        }
        var newline = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).ToString();
        if (newline.Length == 0) { newline = "\n"; }
        var stub = Stub(type.Identifier.ValueText, operations).Replace("\n", newline);
        var first = root.GetFirstToken(includeZeroWidth: true);
        var leading = ParseLeadingTrivia(stub).AddRange(first.LeadingTrivia);
        return document.WithSyntaxRoot(root.ReplaceToken(first, first.WithLeadingTrivia(leading)));
    }
}
