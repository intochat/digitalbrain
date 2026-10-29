using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// DB0028: in a file that declares an app, <c>#pragma warning disable|restore</c> naming a <c>DBnnnn</c> id and
/// <c>[SuppressMessage]</c> / <c>[UnconditionalSuppressMessage]</c> with a DigitalBrain category or a DB check id.
/// The diagnostic sits on the directive itself, so the directive cannot hide it (T-08-04b).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SuppressionAnalyzer : DiagnosticAnalyzer
{
    private const string SuppressMessage = "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute";
    private const string UnconditionalSuppressMessage = "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.Suppression);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSemanticModelAction(Analyze);
    }

    private static void Analyze(SemanticModelAnalysisContext context)
    {
        var model = context.SemanticModel;
        var root = model.SyntaxTree.GetRoot(context.CancellationToken);
        if (!DeclaresApp(model, root, context)) { return; }

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.GetStructure() is not PragmaWarningDirectiveTriviaSyntax pragma) { continue; }
            foreach (var code in pragma.ErrorCodes)
            {
                var id = code is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : code.ToString();
                if (IsDbId(id))
                {
                    context.ReportDiagnostic(Diagnostic.Create(AppRules.Suppression, pragma.GetLocation(), id));
                }
            }
        }

        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            var type = model.GetTypeInfo(attribute, context.CancellationToken).Type as INamedTypeSymbol;
            var name = Rules.FullName(type);
            if (name != SuppressMessage && name != UnconditionalSuppressMessage) { continue; }
            var arguments = attribute.ArgumentList?.Arguments;
            if (arguments is null || arguments.Value.Count < 2) { continue; }
            var category = model.GetConstantValue(arguments.Value[0].Expression, context.CancellationToken).Value as string ?? string.Empty;
            var checkId = model.GetConstantValue(arguments.Value[1].Expression, context.CancellationToken).Value as string ?? string.Empty;
            var colon = checkId.IndexOf(':');
            var id = (colon >= 0 ? checkId.Substring(0, colon) : checkId).Trim();
            if (IsDbId(id) || category.StartsWith("DigitalBrain", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(AppRules.Suppression, attribute.GetLocation(), id));
            }
        }
    }

    private static bool DeclaresApp(SemanticModel model, SyntaxNode root, SemanticModelAnalysisContext context)
    {
        foreach (var declaration in root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax).OfType<ClassDeclarationSyntax>())
        {
            if (AppRules.IsApp(model.GetDeclaredSymbol(declaration, context.CancellationToken))) { return true; }
        }
        return false;
    }

    private static bool IsDbId(string id)
    {
        if (id.Length != 6 || !id.StartsWith("DB", StringComparison.Ordinal)) { return false; }
        for (var i = 2; i < 6; i++)
        {
            if (id[i] < '0' || id[i] > '9') { return false; }
        }
        return true;
    }
}
