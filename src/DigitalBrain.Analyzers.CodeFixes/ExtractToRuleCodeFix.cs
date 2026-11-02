using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Analyzers.Neurons;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Analyzers.CodeFixes;

/// <summary>
/// DB0040 fix: <c>if (condition) throw new X("message");</c> in an async method becomes <c>await Check("method", new Name(...));</c>
/// with <c>Rules/Name.cs</c> holding the rule record. Locals and parameters in the condition and message become record parameters;
/// fields and properties are read at the call site. Not offered for an else branch, a non-async method, or a condition that
/// calls, awaits, assigns or captures a lambda.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExtractToRuleFix)), Shared]
public sealed class ExtractToRuleFix : CodeFixProvider
{
    /// <summary>The equivalence key of the action.</summary>
    public const string Key = "DB0040.ExtractToRule";

    private static readonly string[] Folders = ["Rules"];

    // Rule and record members a positional parameter would hide or duplicate.
    private static readonly ImmutableHashSet<string> Reserved = ImmutableHashSet.Create(
        "Name", "Broken", "Message", "EqualityContract", "ToString", "GetHashCode", "Equals", "PrintMembers", "Deconstruct");

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(Rules.BareInvariant.Id);

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) { return; }
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ThrowStatementSyntax thrown) { continue; }
            if (Plan(model, thrown, context.CancellationToken) is not { } plan) { continue; }
            context.RegisterCodeFix(CodeAction.Create("Extract to rule", ct => Apply(context.Document, root, plan), Key), diagnostic);
        }
    }

    private sealed class Extraction(IfStatementSyntax guard, string name, string text, string call)
    {
        public IfStatementSyntax Guard { get; } = guard;
        public string Name { get; } = name;
        public string Text { get; } = text;
        public string Call { get; } = call;
    }

    private static Extraction? Plan(SemanticModel model, ThrowStatementSyntax thrown, CancellationToken cancellation)
    {
        var guard = thrown.Parent as IfStatementSyntax
            ?? (thrown.Parent is BlockSyntax { Statements.Count: 1 } block ? block.Parent as IfStatementSyntax : null);
        if (guard is null || guard.Else is not null) { return null; }
        if (guard.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method || !method.Modifiers.Any(SyntaxKind.AsyncKeyword)) { return null; }
        if (thrown.Expression is not BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } creation) { return null; }
        var message = creation.ArgumentList!.Arguments[0].Expression;
        if (!BareInvariantAnalyzer.IsMessage(message)) { return null; }
        var owner = model.GetDeclaredSymbol(method, cancellation)?.ContainingType;
        if (owner is null) { return null; }
        var awaits = Rules.DerivesFrom(owner, "DigitalBrain.Neuron");

        var captures = new List<(ExpressionSyntax Node, string Name, string Type)>();
        if (!Capture(model, guard.Condition, captures, cancellation) || !Capture(model, message, captures, cancellation)) { return null; }

        var name = string.Concat(Words(message).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        if (name.Length == 0 || model.Compilation.GetSymbolsWithName(name, SymbolFilter.Type, cancellation).Any()) { return null; }
        var kebab = string.Join("-", Words(message).Select(w => w.ToLowerInvariant()));

        // One parameter per distinct captured expression, named after its last identifier.
        var parameters = new List<(string Source, string Name, string Type)>();
        var renames = new Dictionary<ExpressionSyntax, string>();
        foreach (var capture in captures)
        {
            var source = capture.Node.ToString();
            var existing = parameters.FindIndex(p => p.Source == source);
            if (existing < 0)
            {
                var unique = Reserved.Contains(capture.Name) ? capture.Name + "Value" : capture.Name;
                for (var n = 2; parameters.Any(p => p.Name == unique) || unique == name || Reserved.Contains(unique); n++) { unique = capture.Name + n; }
                parameters.Add((source, unique, capture.Type));
                existing = parameters.Count - 1;
            }
            renames[capture.Node] = parameters[existing].Name;
        }
        string Rewrite(ExpressionSyntax expression) => expression
            .ReplaceNodes(renames.Keys.Where(k => expression.Contains(k)), (original, _) => SyntaxFactory.IdentifierName(renames[original]).WithTriviaFrom(original))
            .WithoutTrivia().ToString();
        var condition = renames.TryGetValue(guard.Condition, out var whole) ? whole : Rewrite(guard.Condition);

        var newline = guard.SyntaxTree.GetText(cancellation).ToString().Contains("\r\n") ? "\r\n" : "\n";
        // The new file sees the original's usings, so type names in the condition resolve the same way.
        var text = new StringBuilder();
        var usings = guard.SyntaxTree.GetCompilationUnitRoot(cancellation).Usings
            .Concat(guard.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(n => n.Usings))
            .Select(u => u.WithoutTrivia().ToString())
            .Where(u => u != "using DigitalBrain.Rules;").Prepend("using DigitalBrain.Rules;").Distinct().OrderBy(u => u.TrimEnd(';'), System.StringComparer.Ordinal);
        foreach (var directive in usings) { text.Append(directive).Append(newline); }
        text.Append(newline);
        if (!owner.ContainingNamespace.IsGlobalNamespace) { text.Append("namespace ").Append(owner.ContainingNamespace.ToDisplayString()).Append(';').Append(newline).Append(newline); }
        text.Append("internal sealed record ").Append(name).Append('(').Append(string.Join(", ", parameters.Select(p => p.Type + " " + p.Name)))
            .Append(") : Rule(\"").Append(kebab).Append("\")").Append(newline).Append('{').Append(newline)
            .Append("    public override bool Broken => ").Append(condition).Append(';').Append(newline)
            .Append("    public override string Message => ").Append(Rewrite(message)).Append(';').Append(newline)
            .Append('}').Append(newline);
        var call = (awaits ? "await " : "") + "Check(\"" + method.Identifier.ValueText.ToLowerInvariant() + "\", new " + name + "("
            + string.Join(", ", parameters.Select(p => p.Source)) + "));";
        return new Extraction(guard, name, text.ToString(), call);
    }

    // Locals and parameters are captured as themselves; an instance field or property chain (this.X, State.Foo, local.Length) is
    // captured whole and read at the call site. Anything with effects or deferred evaluation makes the condition unanalyzable.
    private static bool Capture(SemanticModel model, SyntaxNode node, List<(ExpressionSyntax, string, string)> captures, CancellationToken cancellation)
    {
        if (node is InvocationExpressionSyntax or AwaitExpressionSyntax or AnonymousFunctionExpressionSyntax or AssignmentExpressionSyntax
            or ThisExpressionSyntax or BaseExpressionSyntax
            || node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression)
            || node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression)) { return false; }
        if (node is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            var expression = (ExpressionSyntax)node;
            var symbol = model.GetSymbolInfo(expression, cancellation).Symbol;
            var captured = symbol switch
            {
                ILocalSymbol or IParameterSymbol => node is IdentifierNameSyntax,
                // Static and const members are read at the call site too: the record file cannot name the owner's members.
                IFieldSymbol or IPropertySymbol => true,
                _ => false,
            };
            // A method group, or a type the new file cannot see (nested in the owner, or not accessible in the assembly), declines the fix.
            if (symbol is IMethodSymbol) { return false; }
            if (symbol is ITypeSymbol named && (named.ContainingType is not null || !model.Compilation.IsSymbolAccessibleWithin(named, model.Compilation.Assembly))) { return false; }
            if (captured)
            {
                var type = model.GetTypeInfo(expression, cancellation).Type;
                if (type is null || type.TypeKind == TypeKind.Error || !Visible(type, model.Compilation)) { return false; }
                captures.Add((expression, symbol!.Name, type.ToDisplayString()));
                return true;
            }
            if (node is MemberAccessExpressionSyntax access) { return Capture(model, access.Expression, captures, cancellation); }
            return true;
        }
        return node.ChildNodes().All(child => Capture(model, child, captures, cancellation));
    }

    // A parameter type the new file can name: accessible in the assembly, and so are its element and type arguments.
    private static bool Visible(ITypeSymbol type, Compilation compilation) => type switch
    {
        IArrayTypeSymbol array => Visible(array.ElementType, compilation),
        ITypeParameterSymbol => false,
        INamedTypeSymbol named => compilation.IsSymbolAccessibleWithin(named, compilation.Assembly)
            && named.TypeArguments.All(argument => Visible(argument, compilation)),
        _ => compilation.IsSymbolAccessibleWithin(type, compilation.Assembly),
    };

    // The first five words of the message, letters only.
    private static IEnumerable<string> Words(ExpressionSyntax message)
    {
        var text = message is LiteralExpressionSyntax literal
            ? literal.Token.ValueText
            : string.Join(" ", ((InterpolatedStringExpressionSyntax)message).Contents.Select(c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : " "));
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in text + " ")
        {
            if (char.IsLetter(ch)) { current.Append(ch); continue; }
            if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }
        return words.Take(5);
    }

    private static Task<Solution> Apply(Document document, SyntaxNode root, Extraction plan)
    {
        var replacement = SyntaxFactory.ParseStatement(plan.Call).WithTriviaFrom(plan.Guard);
        var project = document.WithSyntaxRoot(root.ReplaceNode(plan.Guard, replacement)).Project;
        var directory = document.FilePath is { } path ? Path.GetDirectoryName(path) : null;
        var file = plan.Name + ".cs";
        var added = project.AddDocument(file, SourceText.From(plan.Text, Encoding.UTF8), Folders,
            directory is null ? null : Path.Combine(directory, "Rules", file));
        return Task.FromResult(added.Project.Solution);
    }
}
