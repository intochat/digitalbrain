using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
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

/// <summary>DB0020 fix: derive the <c>[App]</c> class from <c>DigitalBrain.App</c> (replaces a class base, else adds one).</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AppBaseFix)), Shared]
public sealed class AppBaseFix : CodeFixProvider
{
    /// <summary>The equivalence key of the action.</summary>
    public const string Key = "DB0020.DeriveFromApp";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AppRules.AppBase.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ClassDeclarationSyntax>() is not { } type) { continue; }
            context.RegisterCodeFix(
                CodeAction.Create("Derive from App", ct => Fix(context.Document, type, ct), Key), diagnostic);
        }
    }

    private static async Task<Document> Fix(Document document, ClassDeclarationSyntax type, CancellationToken cancellation)
    {
        var model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);
        var app = SimpleBaseType(ParseTypeName("DigitalBrain.App").WithAdditionalAnnotations(Simplifier.Annotation));
        ClassDeclarationSyntax next;
        if (type.BaseList is { Types.Count: > 0 } list)
        {
            var first = list.Types[0];
            var isClass = model?.GetTypeInfo(first.Type, cancellation).Type is { TypeKind: TypeKind.Class or TypeKind.Error };
            next = isClass
                ? type.WithBaseList(list.WithTypes(list.Types.Replace(first, app.WithTriviaFrom(first))))
                : type.WithBaseList(list.WithTypes(list.Types.Insert(0, app)));
        }
        else
        {
            var identifier = type.TypeParameterList is null ? type.Identifier.WithTrailingTrivia() : type.Identifier;
            next = type.WithIdentifier(identifier)
                .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(app))
                    .WithColonToken(Token(SyntaxKind.ColonToken).WithLeadingTrivia(Space).WithTrailingTrivia(Space))
                    .WithTrailingTrivia(type.Identifier.TrailingTrivia));
        }
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        var changed = document.WithSyntaxRoot(root!.ReplaceNode(type, next));
        return await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: cancellation).ConfigureAwait(false);
    }
}

/// <summary>
/// DB0022 fixes: make the swallowing catch rethrow (<c>throw;</c>), or, inside an instance member of an App, record the error state
/// (<c>catch (Exception error) { RecordError(error); }</c>).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EmptyCatchFix)), Shared]
public sealed class EmptyCatchFix : CodeFixProvider
{
    /// <summary>The equivalence key of the rethrow action.</summary>
    public const string Key = "DB0022.Rethrow";

    /// <summary>The equivalence key of the record-the-error-state action.</summary>
    public const string RecordErrorKey = RecordErrorAction.Key;

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AppRules.SwallowingCatch.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<CatchClauseSyntax>() is not { } clause) { continue; }
            context.RegisterCodeFix(CodeAction.Create("Rethrow", ct => Fix(context.Document, clause, ct), Key), diagnostic);
            if (model is not null && RecordErrorAction.Applies(model, clause, context.CancellationToken))
            {
                context.RegisterCodeFix(
                    CodeAction.Create("Record the error state", ct => RecordErrorAction.Fix(context.Document, clause, ct), RecordErrorKey), diagnostic);
            }
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
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AppRules.HandWrittenJson.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node?.FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocation
                || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not AnonymousObjectCreationExpressionSyntax anonymous
                || invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>() is null)
            {
                continue;
            }
            context.RegisterCodeFix(CodeAction.Create("Use a typed record", ct => Fix(context.Document, anonymous, ct), Key), diagnostic);
        }
    }

    private static async Task<Document> Fix(Document document, AnonymousObjectCreationExpressionSyntax anonymous, CancellationToken cancellation)
    {
        var model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
        if (model is null || root is null || model.GetTypeInfo(anonymous, cancellation).Type is not INamedTypeSymbol shape) { return document; }
        var owner = anonymous.FirstAncestorOrSelf<ClassDeclarationSyntax>()!;
        var method = anonymous.FirstAncestorOrSelf<MemberDeclarationSyntax>() switch
        {
            MethodDeclarationSyntax m => m.Identifier.ValueText,
            PropertyDeclarationSyntax p => p.Identifier.ValueText,
            _ => "Json",
        };
        var taken = new HashSet<string>(model.GetDeclaredSymbol(owner, cancellation)?.GetMembers().Select(m => m.Name) ?? Enumerable.Empty<string>());
        var name = method + "Payload";
        for (var i = 2; taken.Contains(name); i++) { name = method + "Payload" + i; }

        var parameters = new List<ParameterSyntax>();
        foreach (var property in shape.GetMembers().OfType<IPropertySymbol>())
        {
            parameters.Add(Parameter(Identifier(property.Name))
                .WithType(ParseTypeName(property.Type.ToMinimalDisplayString(model, anonymous.SpanStart))));
        }
        var record = RecordDeclaration(Token(SyntaxKind.RecordKeyword), Identifier(name))
            .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.SealedKeyword)))
            .WithParameterList(ParameterList(SeparatedList(parameters)))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var creation = ObjectCreationExpression(IdentifierName(name))
            .WithArgumentList(ArgumentList(SeparatedList(anonymous.Initializers.Select(i => Argument(i.Expression.WithoutTrivia())))))
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
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AppRules.MissingSpec.Id);

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
                    operations.Add(DigitalBrain.Apps.SpecComment.NormalizeOperation(method.Name));
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
