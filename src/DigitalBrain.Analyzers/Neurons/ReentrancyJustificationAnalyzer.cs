using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Neurons;

/// <summary>
/// DB0032: Neuron reentrancy requires explicit justification comment explaining why concurrent turn interleaving is safe.
/// Excludes test assemblies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReentrancyJustificationAnalyzer : DiagnosticAnalyzer
{
    private const string ReentrantAttribute = "DigitalBrain.Neurons.ReentrantAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.ReentrancyJustification);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!Rules.Has(type, ReentrantAttribute) && !Rules.Has(type, "DigitalBrain.ReentrantAttribute"))
        {
            return;
        }

        if (TestProjectExclusion.IsTestProject(context.Compilation, context.Options) ||
            TestProjectExclusion.IsInTestScope(type, context.Compilation, context.Options))
        {
            return;
        }

        var hasJustification = false;
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax(context.CancellationToken);
            var triviaList = syntax.GetLeadingTrivia();
            foreach (var trivia in triviaList)
            {
                if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                    trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                    trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                    trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    var text = trivia.ToFullString().Trim();
                    if (text.Length > 2)
                    {
                        hasJustification = true;
                        break;
                    }
                }
            }

            if (hasJustification)
            {
                break;
            }
        }

        if (!hasJustification)
        {
            var loc = type.Locations.Length > 0 ? type.Locations[0] : Location.None;
            context.ReportDiagnostic(Diagnostic.Create(Rules.ReentrancyJustification, loc, type.Name));
        }
    }
}
